using System;

namespace N7.Utilities
{
    // Pure-math euler<->quaternion conversion ported verbatim from
    // tools/sector-editor/Utilities/QuaternionCalc.cs. Tier 12e sprites
    // (Mob/Planet/etc.) use this for the orientation_u/v/w/z DB columns
    // shown in the property pane as pitch/yaw/roll.
    class QuaternionCalc
    {
        public double[] AngleToQuat(double headingDeg, double attitudeDeg, double bankDeg)
        {
            double[] quat4d = new double[4];

            double heading  = (Math.PI / 180) * headingDeg;
            double attitude = (Math.PI / 180) * attitudeDeg;
            double bank     = (Math.PI / 180) * bankDeg;

            double c1 = Math.Cos(heading / 2);
            double s1 = Math.Sin(heading / 2);
            double c2 = Math.Cos(attitude / 2);
            double s2 = Math.Sin(attitude / 2);
            double c3 = Math.Cos(bank / 2);
            double s3 = Math.Sin(bank / 2);

            double c1c2 = c1 * c2;
            double s1s2 = s1 * s2;

            quat4d[0] = c1c2 * c3 - s1s2 * s3;            // z
            quat4d[1] = c1c2 * s3 + s1s2 * c3;            // u
            quat4d[2] = s1 * c2 * c3 + c1 * s2 * s3;      // v
            quat4d[3] = c1 * s2 * c3 - s1 * c2 * s3;      // w

            return quat4d;
        }

        public double[] QuatToAngle(double[] q1)
        {
            double[] vec3d = new double[3];
            double sqw = q1[0] * q1[0];
            double sqx = q1[1] * q1[1];
            double sqy = q1[2] * q1[2];
            double sqz = q1[3] * q1[3];
            double unit = sqx + sqy + sqz + sqw;
            double test = q1[1] * q1[2] + q1[3] * q1[0];

            if (test > 0.499 * unit)
            {
                vec3d[0] = 2 * Math.Atan2(q1[1], q1[0]);
                vec3d[1] = Math.PI / 2;
                vec3d[2] = 0;
                return vec3d;
            }
            if (test < -0.499 * unit)
            {
                vec3d[0] = -2 * Math.Atan2(q1[1], q1[0]);
                vec3d[1] = -Math.PI / 2;
                vec3d[2] = 0;
                return vec3d;
            }

            double yaw   = Math.Atan2(2 * q1[2] * q1[0] - 2 * q1[1] * q1[3], sqx - sqy - sqz + sqw);
            double pitch = Math.Asin(2 * test / unit);
            double roll  = Math.Atan2(2 * q1[1] * q1[0] - 2 * q1[2] * q1[3], -sqx + sqy - sqz + sqw);

            vec3d[0] = (180 / Math.PI) * yaw;
            vec3d[1] = (180 / Math.PI) * pitch;
            vec3d[2] = (180 / Math.PI) * roll;

            return vec3d;
        }
    }
}
