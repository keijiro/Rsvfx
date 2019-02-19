using UnityEngine;
using Intel.RealSense;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Rsvfx
{
    public class PointCloudBaker : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] RsFrameProvider _colorSource = null;
        [SerializeField] RsFrameProvider _pointSource = null;
        [Space]
        [SerializeField] RenderTexture _colorMap = null;
        [SerializeField] RenderTexture _positionMap = null;

        [SerializeField, HideInInspector] ComputeShader _compute = null;

        #endregion

        #region Private fields

        FrameQueue _colorQueue;
        FrameQueue _pointQueue;

        ComputeBuffer _colorBuffer;
        ComputeBuffer _positionBuffer;
        ComputeBuffer _remapBuffer;

        Vector2Int _dimensions;
        RenderTexture _tempColorMap;
        RenderTexture _tempPositionMap;

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            _colorQueue = new FrameQueue(1);
            _pointQueue = new FrameQueue(1);

            _colorSource.OnNewSample += OnNewColorSample;
            _pointSource.OnNewSample += OnNewPointSample;
        }

        void OnDestroy()
        {
            if (_colorQueue != null)
            {
                _colorQueue.Dispose();
                _colorQueue = null;
            }

            if (_pointQueue != null)
            {
                _pointQueue.Dispose();
                _pointQueue = null;
            }

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
                Destroy(_tempColorMap);
                _tempColorMap = null;
            }

            if (_tempPositionMap != null)
            {
                Destroy(_tempPositionMap);
                _tempPositionMap = null;
            }
        }

        void Update()
        {
            using (var frame = DequeueColorFrame())
                if (frame != null) UpdateColorData(frame);

            using (var frame = DequeuePointFrame())
                if (frame != null) UpdatePointData(frame);

            if (_colorBuffer == null) return;
            if (_positionBuffer == null) return;
            if (_remapBuffer == null) return;

            if (_colorMap == null) return;
            if (_positionMap == null) return;

            if (!CheckConsistency()) return;

            RemapPoints();

            Graphics.CopyTexture(_tempColorMap, _colorMap);
            Graphics.CopyTexture(_tempPositionMap, _positionMap);
        }

        #endregion

        #region Frame provider callback

        void OnNewColorSample(Frame frame)
        {
            using (var cf = RetrieveColorFrame(frame))
                if (cf != null) _colorQueue.Enqueue(cf);
        }

        void OnNewPointSample(Frame frame)
        {
            using (var pf = RetrievePointFrame(frame))
                if (pf != null) _pointQueue.Enqueue(pf);
        }

        #endregion

        #region Frame query method

        VideoFrame RetrieveColorFrame(Frame frame)
        {
            if (frame is VideoFrame)
            {
                using (var profile = frame.Profile)
                {
                    if (profile.Stream == Stream.Color &&
                        profile.Format == Format.Rgba8 &&
                        profile.Index == 0)
                        return (VideoFrame)frame;
                }
            }

            if (frame.IsComposite)
            {
                using (var fset = FrameSet.FromFrame(frame))
                {
                    foreach (var f in fset)
                    {
                        var ret = RetrieveColorFrame(f);
                        if (ret != null) return ret;
                        f.Dispose();
                    }
                }
            }

            return null;
        }

        Points RetrievePointFrame(Frame frame)
        {
            if (frame is Points) return (Points)frame;

            if (frame.IsComposite)
            {
                using (var fset = FrameSet.FromFrame(frame))
                {
                    foreach (var f in fset)
                    {
                        var ret = RetrievePointFrame(f);
                        if (ret != null) return ret;
                        f.Dispose();
                    }
                }
            }

            return null;
        }

        #endregion

        #region Private properties and methods

        VideoFrame DequeueColorFrame()
        {
            Frame frame;
            return _colorQueue.PollForFrame(out frame) ? (VideoFrame)frame : null;
        }

        Points DequeuePointFrame()
        {
            Frame frame;
            return _pointQueue.PollForFrame(out frame) ? (Points)frame : null;
        }

        void UpdateColorData(VideoFrame frame)
        {
            if (frame.Data == System.IntPtr.Zero) return;

            var size = frame.Width * frame.Height;

            if (_colorBuffer != null && _colorBuffer.count != size)
            {
                _colorBuffer.Dispose();
                _colorBuffer = null;
            }

            if (_colorBuffer == null)
                _colorBuffer = new ComputeBuffer(size, 4);

            UnsafeUtility.SetUnmanagedData (_colorBuffer, frame.Data, size, 4);

            _dimensions = new Vector2Int(frame.Width, frame.Height);
        }

        void UpdatePointData(Points points)
        {
            if (points.VertexData == System.IntPtr.Zero) return;
            if (points.TextureData == System.IntPtr.Zero) return;

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
        }

        void RemapPoints()
        {
            if (_tempColorMap != null &&
                (_tempColorMap.width != _dimensions.x ||
                 _tempColorMap.height != _dimensions.y))
            {
                Destroy(_tempColorMap);
                _tempColorMap = null;
            }

            if (_tempPositionMap != null &&
                (_tempPositionMap.width != _dimensions.x ||
                 _tempPositionMap.height != _dimensions.y))
            {
                Destroy(_tempPositionMap);
                _tempPositionMap = null;
            }

            if (_tempColorMap == null)
            {
                _tempColorMap = new RenderTexture
                    (_dimensions.x, _dimensions.y, 0, RenderTextureFormat.ARGB32);
                _tempColorMap.enableRandomWrite = true;
                _tempColorMap.Create();
            }

            if (_tempPositionMap == null)
            {
                _tempPositionMap = new RenderTexture
                    (_dimensions.x, _dimensions.y, 0, RenderTextureFormat.ARGBHalf);
                _tempPositionMap.enableRandomWrite = true;
                _tempPositionMap.Create();
            }

            _compute.SetInts("MapDimensions", _dimensions);
            _compute.SetBuffer(0, "ColorBuffer", _colorBuffer);
            _compute.SetBuffer(0, "PositionBuffer", _positionBuffer);
            _compute.SetBuffer(0, "RemapBuffer", _remapBuffer);
            _compute.SetTexture(0, "ColorMap", _tempColorMap);
            _compute.SetTexture(0, "PositionMap", _tempPositionMap);
            _compute.Dispatch(0, _dimensions.x / 8, _dimensions.y / 8, 1);
        }

        bool _warned;

        bool CheckConsistency()
        {
            if (_warned) return false;

            if (_dimensions.x % 8 != 0 && _dimensions.y % 8 != 0)
            {
                Debug.LogError("Color input dimensions should be a multiple of 8.");
                _warned = true;
            }

            if (_colorMap.width != _dimensions.x || _colorMap.height != _dimensions.y)
            {
                Debug.LogError("Color map dimensions don't match with the input.");
                _warned = true;
            }

            if (_positionMap.width != _dimensions.x || _positionMap.height != _dimensions.y)
            {
                Debug.LogError("Position map dimensions don't match with the input.");
                _warned = true;
            }

            if (_colorMap.format != RenderTextureFormat.ARGB32)
            {
                Debug.LogError("Color map format should be ARGB32");
                _warned = true;
            }

            if (_positionMap.format != RenderTextureFormat.ARGBHalf)
            {
                Debug.LogError("Position map format should be ARGBHalf");
                _warned = true;
            }

            return !_warned;
        }

        #endregion
    }
}
