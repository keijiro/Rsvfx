using UnityEngine;
using Intel.RealSense;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Rsvfx
{
    public class PointCloudBaker : MonoBehaviour
    {
        [SerializeField] RsFrameProvider _source = null;
        [SerializeField] ComputeShader _compute = null;

        FrameQueue _queue;
        ComputeBuffer _buffer;
        RenderTexture _texture;

        #region MonoBehaviour implementation

        void Start()
        {
            CheckComputeConsistency();
            _queue = new FrameQueue(1);
            _source.OnNewSample += OnNewSample;
        }

        void OnDestroy()
        {
            if (_queue != null)
            {
                _queue.Dispose();
                _queue = null;
            }

            if (_buffer != null)
            {
                _buffer.Dispose();
                _buffer = null;
            }

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }
        }

        void Update()
        {
            using (var points = DequeuePoints())
            {
                if (points == null) return;
                if (points.VertexData == System.IntPtr.Zero) return;

                if (_buffer == null || _buffer.count != points.Count * 3)
                    ResetBuffers(points);

                ComputeBufferUtility.SetUnmanagedData
                    (_buffer, points.VertexData, points.Count * 3, sizeof(float));

                _compute.SetBuffer(0, "Input", _buffer);
                _compute.SetTexture(0, "Output", _texture);
                _compute.Dispatch(0, 1, _texture.height, 1);
            }
        }

        #endregion

        #region Private properties and methods

        const int _textureWidth = 256;

        void CheckComputeConsistency()
        {
            uint x, y, z;
            _compute.GetKernelThreadGroupSizes(0, out x, out y, out z);
            UnityEngine.Assertions.Assert.IsTrue(x == _textureWidth && y == 1 && z == 1);
        }

        Points DequeuePoints()
        {
            Frame frame;
            return _queue.PollForFrame(out frame) ? (Points)frame : null;
        }

        void ResetBuffers(Points points)
        {
            if (_buffer != null) _buffer.Dispose();
            if (_texture != null) Destroy(_texture);

            _buffer = new ComputeBuffer(points.Count * 3, sizeof(float));

            _texture = new RenderTexture(
                _textureWidth, points.Count / _textureWidth, 0,
                RenderTextureFormat.ARGBHalf
            );
            _texture.enableRandomWrite = true;
            _texture.Create();
        }

        #endregion

        #region Frame provider callback

        void OnNewSample(Frame frame)
        {
            try
            {
                using (var points = RetrievePoints(frame))
                {
                    if (points != null) _queue.Enqueue(points);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }

            frame.Dispose();
        }

        #endregion

        #region Frame query method

        Points RetrievePoints(Frame frame)
        {
            if (frame is Points) return (Points)frame;

            if (frame.IsComposite)
            {
                using (var fset = FrameSet.FromFrame(frame))
                {
                    foreach (var f in fset)
                    {
                        if (f is Points) return (Points)f;
                        f.Dispose();
                    }
                }
            }

            return null;
        }

        #endregion
    }
}
