using System;
using OpenTK.Graphics.OpenGL4;

namespace SolarSystemApp.Rendering.Modern
{
    internal sealed class SceneFbo : IDisposable
    {
        public int Framebuffer { get; private set; }
        public int ColorTexture { get; private set; }
        public int DepthTexture { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public void Ensure(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (Framebuffer != 0 && width == Width && height == Height)
                return;

            Width = width;
            Height = height;

            if (Framebuffer == 0)
                Framebuffer = GL.GenFramebuffer();
            if (ColorTexture == 0)
                ColorTexture = GL.GenTexture();
            if (DepthTexture == 0)
                DepthTexture = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture2D, ColorTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba16f, Width, Height, 0, PixelFormat.Rgba, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.BindTexture(TextureTarget.Texture2D, DepthTexture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.DepthComponent24, Width, Height, 0, PixelFormat.DepthComponent, PixelType.Float, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, ColorTexture, 0);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, DepthTexture, 0);
            GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void Bind()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, Framebuffer);
            GL.Viewport(0, 0, Width, Height);
        }

        public void Dispose()
        {
            if (Framebuffer != 0)
                GL.DeleteFramebuffer(Framebuffer);
            if (ColorTexture != 0)
                GL.DeleteTexture(ColorTexture);
            if (DepthTexture != 0)
                GL.DeleteTexture(DepthTexture);

            Framebuffer = 0;
            ColorTexture = 0;
            DepthTexture = 0;
        }
    }
}
