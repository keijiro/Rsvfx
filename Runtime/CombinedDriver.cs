using UnityEngine;
using Unity.Mathematics;
using Intel.RealSense;
using System.Threading;

namespace Rsvfx
{
    //
    // CombinedDriver
    //
    // A Unity component that manages a combination of a depth camera (D4xx)
    // and a tracker (T265). This class directly uses the C# wrapper of the
    // RealSense SDK, so there is no dependency to the Unity components
    // included in the RealSense SDK.
    //
    // There are two main functionalities in this component:
    // - Updates attribute maps (position/color) based on depth input.
    // - Updates a transform based on pose input from a tracker.
    //
    // Pose input is buffered in an internal queue to synchronize with depth
    // input. Contrastly, depth input is processed immediately after reception.
    //
    public sealed class CombinedDriver : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] uint2 _resolution = math.uint2(640, 480);
        [SerializeField] uint _framerate = 30;
        [Space]
        [SerializeField] float _depthThreshold = 10;
        [SerializeField, Range(0, 1)] float _brightness = 0;
        [SerializeField, Range(0, 1)] float _saturation = 1;
        [Space]
        [SerializeField] Transform _poseTransform = null;
        [SerializeField] RenderTexture _colorMap = null;
        [SerializeField] RenderTexture _positionMap = null;

        [SerializeField, HideInInspector] ComputeShader _compute = null;

        #endregion

        #region Public properties

        public float depthThreshold {
            get { return _depthThreshold; }
            set { _depthThreshold = value; }
        }

        public float brightness {
            get { return _brightness; }
            set { _brightness = value; }
        }

        public float saturation {
            get { return _saturation; }
            set { _saturation = value; }
        }

        #endregion

        #region Private objects

        (Pipeline tracker, Pipeline depth) _pipes;
        (Thread tracker, Thread depth) _threads;
        bool _terminate;

        PoseQueue _poseQueue = new PoseQueue();

        (VideoFrame color, Points point) _depthFrame;
        (Intrinsics color, Intrinsics depth) _intrinsics;
        readonly object _depthFrameLock = new object();

        DepthConverter _converter;
        double _depthTime;

        #endregion

        #region Device pipeline threads

        void TrackerThread()
        {
            while (!_terminate)
            {
                // A tracker may stop sending pose data when V-SLAM fails. This
                // occasionally happens when there is no visual clue in its
                // sight (looking at a white wall, left in a dark room, etc.).
                // To handle this case properly, we use TryWaitForFrames rather
                // than WaitForFrames.
                FrameSet fs;
                if (_pipes.tracker.TryWaitForFrames(out fs))
                {
                    // Retrieve and enqueue the pose data.
                    using (fs)
                        using (var pf = fs.PoseFrame)
                            lock (_poseQueue) _poseQueue.Enqueue(pf);
                }
            }
        }

        void DepthThread()
        {
            using (var pcBlock = new PointCloud())
                while (!_terminate)
                    using (var fs = _pipes.depth.WaitForFrames())
                    {
                        // Retrieve and store the color frame.
                        lock (_depthFrameLock)
                        {
                            _depthFrame.color?.Dispose();
                            _depthFrame.color = fs.ColorFrame;

                            using (var prof = _depthFrame.color.
                                   GetProfile<VideoStreamProfile>())
                                _intrinsics.color = prof.GetIntrinsics();

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

                                using (var prof = df.
                                       GetProfile<VideoStreamProfile>())
                                    _intrinsics.depth = prof.GetIntrinsics();
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
                _converter.LoadColorData(_depthFrame.color, _intrinsics.color);
                _converter.LoadPointData(_depthFrame.point, _intrinsics.depth);
                time = _depthFrame.color.Timestamp;
            }

            // Update the converter options.
            _converter.Brightness = _brightness;
            _converter.Saturation = _saturation;
            _converter.DepthThreshold = _depthThreshold;

            // Update the external attribute maps.
            _converter.UpdateAttributeMaps(_colorMap, _positionMap);

            // Apply pose data to the target transform.
            lock (_poseQueue)
            {
                var data = _poseQueue.Dequeue(_depthTime);
                if (data != null)
                {
                    var pose = (PoseData)data;
                    _poseTransform.position = pose.position;
                    _poseTransform.rotation = pose.rotation;
                }
            }

            // Record the timestamp of the depth frame.
            _depthTime = time;
        }

        #endregion
    }
}
