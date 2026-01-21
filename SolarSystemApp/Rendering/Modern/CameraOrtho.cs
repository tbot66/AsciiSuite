using OpenTK.Mathematics;

namespace SolarSystemApp.Rendering.Modern
{
    internal sealed class CameraOrtho
    {
        public Vector2 Position { get; set; }
        public float Zoom { get; set; } = 10f;
        public float OrbitYScale { get; set; } = 0.55f;
        public int ViewportWidth { get; set; } = 1;
        public int ViewportHeight { get; set; } = 1;

        public Matrix4 ViewProjection { get; private set; } = Matrix4.Identity;
        public Vector4 WorldBounds { get; private set; }

        public void UpdateMatrices()
        {
            float widthWorld = ViewportWidth / MathF.Max(0.0001f, Zoom);
            float heightWorld = ViewportHeight / MathF.Max(0.0001f, Zoom * OrbitYScale);

            float left = Position.X - widthWorld * 0.5f;
            float right = Position.X + widthWorld * 0.5f;
            float bottom = Position.Y - heightWorld * 0.5f;
            float top = Position.Y + heightWorld * 0.5f;

            Matrix4 proj = Matrix4.CreateOrthographicOffCenter(left, right, bottom, top, -1f, 1f);
            ViewProjection = proj;
            WorldBounds = new Vector4(left, bottom, right, top);
        }
    }
}
