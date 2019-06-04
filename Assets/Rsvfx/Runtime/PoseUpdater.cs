using UnityEngine;
using Intel.RealSense;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace Rsvfx
{
    public sealed class PoseUpdater : MonoBehaviour
    {
        #region Editable attributes

        [SerializeField] RsFrameProvider _source = null;
        [SerializeField] PointCloudBaker _timeReference = null;

        #endregion

        #region MonoBehaviour implementation

        void Start()
        {
            _source.OnNewSample += OnNewSample;
        }

        void Update()
        {
            // Find the closest time stamp in the pose queue and apply it to
            // the object transform.

            var refTime = _timeReference.Timestamp;
            var minDist = double.PositiveInfinity;
            var minPose = new Pose();

            lock (_poseQueue) while (_poseQueue.Count > 0)
            {
                var pose = _poseQueue.Dequeue();

                var dist = System.Math.Abs(pose.time - refTime);
                if (dist >= minDist) break;

                // ^^ The last entry must be the closest one, so we can stop
                // scanning and leave the rest of the entries for the next
                // frame (because one of them could be the closest one in the
                // next frame, it's very rare though).

                minDist = dist;
                minPose = pose;
            }

            transform.localPosition = minPose.position;
            transform.localRotation = minPose.rotation;
        }

        #endregion

        #region Frame provider callback

        void OnNewSample(Frame frame)
        {
            using (var p = frame.Profile)
                if (p.Stream == Stream.Pose && p.Format == Format.SixDOF)
                    using (var pf = frame.As<PoseFrame>()) EnqueueFrame(pf);
        }

        #endregion

        #region Pose queue

        // Raw pose data in librealsense
        [StructLayout(LayoutKind.Sequential)]
        struct PoseData
        {
            public Vector3 translation;
            public Vector3 velocity;
            public Vector3 acceleration;
            public Quaternion rotation;
            public Vector3 angular_velocity;
            public Vector3 angular_acceleration;
            public int tracker_confidence;
            public int mapper_confidence;
        }

        // Simplified form of pose data
        struct Pose
        {
            public double time;
            public Vector3 position;
            public Quaternion rotation;
        }

        Queue<Pose> _poseQueue = new Queue<Pose>();

        void EnqueueFrame(PoseFrame frame)
        {
            var pose = new Pose { time = frame.Timestamp };

            PoseData data;
            frame.CopyTo(out data);

            var t = data.translation;
            pose.position = new Vector3(t.x, t.y, -t.z);

            var e = data.rotation.eulerAngles;
            pose.rotation = Quaternion.Euler(-e.x, -e.y, e.z);

            lock (_poseQueue) _poseQueue.Enqueue(pose);
        }

        #endregion
    }
}
