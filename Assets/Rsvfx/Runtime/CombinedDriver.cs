using UnityEngine;
using Unity.Mathematics;
using Intel.RealSense;
using System.Threading;

namespace Rsvfx
{
    public sealed class CombinedDriver : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] uint2 _resolution = math.uint2(640, 480);
        [SerializeField] uint _framerate = 30;
        [SerializeField] Transform _poseTransform = null;
        [SerializeField] RenderTexture _colorMap = null;
        [SerializeField] RenderTexture _positionMap = null;

        [SerializeField, HideInInspector] ComputeShader _compute = null;

        #endregion

        #region Private objects

        (Pipeline tracker, Pipeline depth) _pipes;
        (Thread tracker, Thread depth) _threads;
        bool _terminate;

        PoseQueue _poseQueue = new PoseQueue();

        (VideoFrame color, Points point) _depthFrame;
        readonly object _depthFrameLock = new object();

        DepthConverter _converter;

        #endregion

        #region Device pipeline threads

        void TrackerThread()
        {
            while (!_terminate)
                using (var pf = _pipes.tracker.WaitForFrames().Cast<PoseFrame>())
                    lock (_poseQueue) _poseQueue.Enqueue(pf);
        }

        void DepthThread()
        {
            using (var pcBlock = new PointCloud())
                while (!_terminate)
                    using (var frame = _pipes.depth.WaitForFrames())
                    {
                        using (var fs = frame.AsFrameSet())
                        {
                            // Retrieve and store the color frame.
                            lock (_depthFrameLock)
                            {
                                _depthFrame.color?.Dispose();
                                _depthFrame.color = fs.ColorFrame;
                                pcBlock.MapTexture(_depthFrame.color);
                            }

                            // Construct and store a point cloud.
                            using (var df = fs.DepthFrame)
                            {
                                var pc = pcBlock.Process(df).Cast<Points>();
                                lock (_depthFrameLock)
                                {
                                    _depthFrame.point?.Dispose();
                                    _depthFrame.point = pc;
                                }
                            }
                        }
                    }
        }

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            _pipes = (new Pipeline(), new Pipeline());

            // Tracker pipeline activation
            using (var config = new Config())
            {
                config.EnableStream(Stream.Pose, Format.SixDOF);
                _pipes.tracker.Start(config);
            }

            // Depth camera pipeline activation
            using (var config = new Config())
            {
                var r = (int2)_resolution;
                var fps = (int)_framerate;
                config.EnableStream(Stream.Color, r.x, r.y, Format.Rgba8, fps);
                config.EnableStream(Stream.Depth, r.x, r.y, Format.Z16, fps);
                _pipes.depth.Start(config);
            }

            // Worker thread activation
            _threads = (new Thread(TrackerThread), new Thread(DepthThread));
            _threads.tracker.Start();
            _threads.depth.Start();

            // Local objects initialization
            _converter = new DepthConverter(_compute);
        }

        void OnDestroy()
        {
            // Thread termination
            _terminate = true;
            _threads.tracker?.Join();
            _threads.depth?.Join();
            _threads = (null, null);

            // Depth frame finalization
            _depthFrame.color?.Dispose();
            _depthFrame.point?.Dispose();
            _depthFrame = (null, null);

            // Pipeline termination
            _pipes.tracker?.Dispose();
            _pipes.depth?.Dispose();
            _pipes = (null, null);

            // Local objects finalization
            _converter?.Dispose();
            _converter = null;
        }

        void Update()
        {
            var time = 0.0;

            // Retrieve the depth frame data.
            lock (_depthFrameLock)
            {
                if (_depthFrame.color == null) return;
                if (_depthFrame.point == null) return;
                _converter.UpdateColorData(_depthFrame.color);
                _converter.UpdatePointData(_depthFrame.point);
                time = _depthFrame.color.Timestamp;
            }

            // Update the external attribute maps.
            _converter.UpdateMaps(_colorMap, _positionMap);

            // Apply pose data to the target transform.
            lock (_poseQueue)
            {
                var data = _poseQueue.Dequeue(time);
                if (data != null)
                {
                    var pose = (PoseData)data;
                    _poseTransform.position = pose.position;
                    _poseTransform.rotation = pose.rotation;
                }
            }
        }

        #endregion
    }
}
