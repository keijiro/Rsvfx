using UnityEngine;
using Unity.Mathematics;
using IntPtr = System.IntPtr;
using RealSense = Intel.RealSense;

namespace Rsvfx
{
    // A class that converts a RealSense depth frame (a color video frame and
    // a corresponding point cloud) into attribute maps (color/position) that
    // can be easily fed to a visual effect graph.
    sealed class DepthConverter : System.IDisposable
    {
        #region Public properties

        public float DepthThreshold { get; set; } = 10;
        public float Brightness { get; set; } = 0;
        public float Saturation { get; set; } = 1;

        #endregion

        #region Public methods

        public DepthConverter(ComputeShader compute)
        {
            _compute = compute;
        }

        public void Dispose()
        {
            if (_colorBuffer != null)
            {
                _colorBuffer.Dispose();
                _colorBuffer = null;
            }

            if (_positionBuffer != null)
            {
                _positionBuffer.Dispose();
                _positionBuffer = null;
            }

            if (_remapBuffer != null)
            {
                _remapBuffer.Dispose();
                _remapBuffer = null;
            }

            if (_tempColorMap != null)
            {
                Object.Destroy(_tempColorMap);
                _tempColorMap = null;
            }

            if (_tempPositionMap != null)
            {
                Object.Destroy(_tempPositionMap);
                _tempPositionMap = null;
            }
        }

        // Load color data (a video frame) into the internal buffer.
        public void LoadColorData
            (RealSense.VideoFrame frame, in RealSense.Intrinsics intrinsics)
        {
            if (frame == null) return;
            if (frame.Data == IntPtr.Zero) return;

            var size = frame.Width * frame.Height;

            if (_colorBuffer != null && _colorBuffer.count != size)
            {
                _colorBuffer.Dispose();
                _colorBuffer = null;
            }

            if (_colorBuffer == null)
                _colorBuffer = new ComputeBuffer(size, 4);

            UnsafeUtility.SetUnmanagedData(_colorBuffer, frame.Data, size, 4);

            _intrinsics.color = IntrinsicsToVector(intrinsics);
            _dimensions = math.int2(frame.Width, frame.Height);
        }

        // Load point data (a Points instance) into the internal buffer.
        public void LoadPointData
            (RealSense.Points points, in RealSense.Intrinsics intrinsics)
        {
            if (points == null) return;
            if (points.VertexData == IntPtr.Zero) return;
            if (points.TextureData == IntPtr.Zero) return;

            var countx2 = points.Count * 2;
            var countx3 = points.Count * 3;

            if (_positionBuffer != null && _positionBuffer.count != countx3)
            {
                _positionBuffer.Dispose();
                _positionBuffer = null;
            }

            if (_remapBuffer != null && _remapBuffer.count != countx2)
            {
                _remapBuffer.Dispose();
                _remapBuffer = null;
            }

            if (_positionBuffer == null)
                _positionBuffer = new ComputeBuffer(countx3, sizeof(float));

            if (_remapBuffer == null)
                _remapBuffer = new ComputeBuffer(countx2, sizeof(float));

            UnsafeUtility.SetUnmanagedData
                (_positionBuffer, points.VertexData, countx3, sizeof(float));

            UnsafeUtility.SetUnmanagedData
                (_remapBuffer, points.TextureData, countx2, sizeof(float));

            _intrinsics.depth = IntrinsicsToVector(intrinsics);
        }

        // Update external attribute maps based on the internal buffers.
        public void UpdateAttributeMaps
            (RenderTexture colorMap, RenderTexture positionMap)
        {
            if (colorMap == null) return;
            if (positionMap == null) return;

            if (_colorBuffer == null) return;
            if (_positionBuffer == null) return;
            if (_remapBuffer == null) return;

            if (!CheckConsistency(colorMap, positionMap)) return;

            // We can't directly bake these external render textures due to
            // lack of random-write flag, so temporarily bake to the internal
            // render textures.
            BakeToTempMaps();

            // Then copy them to the destinations.
            Graphics.CopyTexture(_tempColorMap, colorMap);
            Graphics.CopyTexture(_tempPositionMap, positionMap);
        }

        #endregion

        #region Private objects

        ComputeShader _compute;

        ComputeBuffer _colorBuffer;
        ComputeBuffer _positionBuffer;
        ComputeBuffer _remapBuffer;

        RenderTexture _tempColorMap;
        RenderTexture _tempPositionMap;

        (Vector4 color, Vector4 depth) _intrinsics;

        int2 _dimensions;

        #endregion

        #region Private methods

        static Vector4 IntrinsicsToVector(RealSense.Intrinsics i)
        {
            return new Vector4(i.ppx, i.ppy, i.fx, i.fy);
        }

        void BakeToTempMaps()
        {
            if (_tempColorMap != null &&
                (_tempColorMap.width != _dimensions.x ||
                 _tempColorMap.height != _dimensions.y))
            {
                Object.Destroy(_tempColorMap);
                _tempColorMap = null;
            }

            if (_tempPositionMap != null &&
                (_tempPositionMap.width != _dimensions.x ||
                 _tempPositionMap.height != _dimensions.y))
            {
                Object.Destroy(_tempPositionMap);
                _tempPositionMap = null;
            }

            if (_tempColorMap == null)
            {
                _tempColorMap = new RenderTexture(
                    _dimensions.x, _dimensions.y, 0,
                    RenderTextureFormat.ARGB32
                );
                _tempColorMap.enableRandomWrite = true;
                _tempColorMap.Create();
            }

            if (_tempPositionMap == null)
            {
                _tempPositionMap = new RenderTexture(
                    _dimensions.x, _dimensions.y, 0,
                    RenderTextureFormat.ARGBHalf
                );
                _tempPositionMap.enableRandomWrite = true;
                _tempPositionMap.Create();
            }

            _compute.SetInts("MapDimensions", _dimensions);
            _compute.SetVector("ColorIntrinsics", _intrinsics.color);
            _compute.SetVector("DepthIntrinsics", _intrinsics.depth);
            _compute.SetFloat("DepthThreshold", DepthThreshold);
            _compute.SetVector("ColorAdjust", Brightness, Saturation);
            _compute.SetBuffer(0, "ColorBuffer", _colorBuffer);
            _compute.SetBuffer(0, "PositionBuffer", _positionBuffer);
            _compute.SetBuffer(0, "RemapBuffer", _remapBuffer);
            _compute.SetTexture(0, "ColorMap", _tempColorMap);
            _compute.SetTexture(0, "PositionMap", _tempPositionMap);
            _compute.Dispatch(0, _dimensions.x / 8, _dimensions.y / 8, 1);
        }

        bool _warned;

        bool CheckConsistency(RenderTexture colorMap, RenderTexture positionMap)
        {
            if (_warned) return false;

            if (_dimensions.x % 8 != 0 &&
                _dimensions.y % 8 != 0)
            {
                Debug.LogError
                    ("Color input dimensions should be a multiple of 8.");
                _warned = true;
            }

            if (colorMap.width  != _dimensions.x ||
                colorMap.height != _dimensions.y)
            {
                Debug.LogError
                    ("Color map dimensions don't match with the input.");
                _warned = true;
            }

            if (positionMap.width  != _dimensions.x ||
                positionMap.height != _dimensions.y)
            {
                Debug.LogError
                    ("Position map dimensions don't match with the input.");
                _warned = true;
            }

            if (colorMap.format != RenderTextureFormat.ARGB32)
            {
                Debug.LogError("Color map format should be ARGB32");
                _warned = true;
            }

            if (positionMap.format != RenderTextureFormat.ARGBHalf)
            {
                Debug.LogError("Position map format should be ARGBHalf");
                _warned = true;
            }

            return !_warned;
        }

        #endregion
    }
}
