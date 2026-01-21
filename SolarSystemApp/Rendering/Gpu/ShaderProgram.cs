using System;
using OpenTK.Graphics.OpenGL4;

namespace SolarSystemApp.Rendering.Gpu
{
    public sealed class ShaderProgram : IDisposable
    {
        public int Handle { get; }

        public ShaderProgram(string vertexSource, string fragmentSource)
        {
            int vertex = CompileShader(ShaderType.VertexShader, vertexSource);
            int fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);

            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vertex);
            GL.AttachShader(Handle, fragment);
            GL.LinkProgram(Handle);

            GL.GetProgram(Handle, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                string info = GL.GetProgramInfoLog(Handle);
                GL.DeleteProgram(Handle);
                GL.DeleteShader(vertex);
                GL.DeleteShader(fragment);
                throw new InvalidOperationException($"Shader link failed: {info}");
            }

            GL.DetachShader(Handle, vertex);
            GL.DetachShader(Handle, fragment);
            GL.DeleteShader(vertex);
            GL.DeleteShader(fragment);
        }

        public void Use()
        {
            GL.UseProgram(Handle);
        }

        public void SetUniform(string name, int value)
        {
            int loc = GL.GetUniformLocation(Handle, name);
            if (loc >= 0)
                GL.Uniform1(loc, value);
        }

        public void SetUniform(string name, float value)
        {
            int loc = GL.GetUniformLocation(Handle, name);
            if (loc >= 0)
                GL.Uniform1(loc, value);
        }

        public void SetUniform(string name, float x, float y)
        {
            int loc = GL.GetUniformLocation(Handle, name);
            if (loc >= 0)
                GL.Uniform2(loc, x, y);
        }

        public void SetUniform(string name, float x, float y, float z)
        {
            int loc = GL.GetUniformLocation(Handle, name);
            if (loc >= 0)
                GL.Uniform3(loc, x, y, z);
        }

        public void Dispose()
        {
            if (Handle != 0)
                GL.DeleteProgram(Handle);
        }

        private static int CompileShader(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string info = GL.GetShaderInfoLog(shader);
                GL.DeleteShader(shader);
                throw new InvalidOperationException($"{type} compile failed: {info}");
            }

            return shader;
        }
    }
}
