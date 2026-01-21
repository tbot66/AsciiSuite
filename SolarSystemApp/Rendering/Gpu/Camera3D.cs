using OpenTK.Mathematics;

namespace SolarSystemApp.Rendering.Gpu
{
    internal sealed class Camera3D
    {
        public Vector3 Position { get; set; }
        public Quaternion Orientation { get; set; }
        public float FieldOfViewRadians { get; set; }
        public float AspectRatio { get; set; }
        public float NearPlane { get; set; }
        public float FarPlane { get; set; }

        public Camera3D(
            Vector3 position,
            Quaternion orientation,
            float fieldOfViewRadians,
            float aspectRatio,
            float nearPlane,
            float farPlane)
        {
            Position = position;
            Orientation = orientation;
            FieldOfViewRadians = fieldOfViewRadians;
            AspectRatio = aspectRatio;
            NearPlane = nearPlane;
            FarPlane = farPlane;
        }

        public static Camera3D CreatePerspective(
            Vector3 position,
            Quaternion orientation,
            float fieldOfViewRadians,
            float aspectRatio,
            float nearPlane,
            float farPlane)
        {
            return new Camera3D(position, orientation, fieldOfViewRadians, aspectRatio, nearPlane, farPlane);
        }

        public Vector3 Forward => Vector3.Normalize(Vector3.Transform(-Vector3.UnitZ, Orientation));

        public Vector3 Up => Vector3.Normalize(Vector3.Transform(Vector3.UnitY, Orientation));

        public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Up));

        public Matrix4 View => Matrix4.LookAt(Position, Position + Forward, Up);

        public Matrix4 Projection => Matrix4.CreatePerspectiveFieldOfView(FieldOfViewRadians, AspectRatio, NearPlane, FarPlane);
    }
}
