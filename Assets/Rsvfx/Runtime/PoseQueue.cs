using Unity.Mathematics;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using RealSense = Intel.RealSense;

namespace Rsvfx
{
    // Simplified pose data structure
    struct PoseData
    {
        public double timestamp;
        public float3 position;
        public quaternion rotation;
    }

    // A queue class that stores incoming pose data. This is needed to
    // synchronize data from tracker and depth camera.
    sealed class PoseQueue
    {
        #region Public methods

        public void Enqueue(RealSense.PoseFrame frame)
        {
            DevicePoseData data;
            frame.CopyTo(out data);

            _queue.Enqueue(new PoseData {
                timestamp = frame.Timestamp,
                position = data.translation * math.float3(1, 1, -1),
                rotation = data.rotation * math.float4(-1, -1, 1, 1)
            });

            // Abandon old entries.
            while (_queue.Count > 30) _queue.Dequeue();
        }

        public PoseData? Dequeue(double referenceTime)
        {
            if (_queue.Count == 0) return null;

            // Find the closest time stamp in the pose queue and apply it to
            // the object transform.

            var minDist = double.PositiveInfinity;
            var minPose = new PoseData();

            while (_queue.Count > 0)
            {
                var pose = _queue.Dequeue();

                var dist = System.Math.Abs(pose.timestamp - referenceTime);
                if (dist >= minDist) break;

                // ^^ The last entry must be the closest one, so we can stop
                // scanning and leave the rest of the entries for the next
                // frame (because one of them could be the closest one in the
                // next frame, it's very rare though).

                minDist = dist;
                minPose = pose;
            }

            return minPose;
        }

        #endregion

        #region Private members

        Queue<PoseData> _queue = new Queue<PoseData>();

        // Pose data structure used in librealsense
        [StructLayout(LayoutKind.Sequential)]
        struct DevicePoseData
        {
            public float3 translation;
            public float3 velocity;
            public float3 acceleration;
            public float4 rotation;
            public float3 angular_velocity;
            public float3 angular_acceleration;
            public int tracker_confidence;
            public int mapper_confidence;
        }

        #endregion
    }
}
