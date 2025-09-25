using OpenTK.Graphics.OpenGL;

namespace Vecxy.Rendering;

public class Shader
{
    private string _vertex = @"
#version 330 core

layout(location = 0) in vec3 aPosition;

void main()
{
    gl_Position = vec4(aPosition, 1.0);
}";

    private string _fragment = @"
#version 330 core

out vec4 fragColor;

void main()
{
    fragColor = vec4(1.0, 1.0, 1.0, 1.0); // Белый цвет
}";

    public int ProgramHandle { get; private set; }

    public void Setup()
    {
        // Вершинный шейдер
        var vertexHandle = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexHandle, _vertex);
        GL.CompileShader(vertexHandle);
        CheckCompileErrors(vertexHandle, "VERTEX");

        // Фрагментный шейдер
        var fragmentHandle = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentHandle, _fragment);
        GL.CompileShader(fragmentHandle);
        CheckCompileErrors(fragmentHandle, "FRAGMENT");

        // Шейдерная программа
        ProgramHandle = GL.CreateProgram();
        GL.AttachShader(ProgramHandle, vertexHandle);
        GL.AttachShader(ProgramHandle, fragmentHandle);
        GL.LinkProgram(ProgramHandle);
        CheckLinkErrors(ProgramHandle);

        // Очистка
        GL.DetachShader(ProgramHandle, vertexHandle);
        GL.DetachShader(ProgramHandle, fragmentHandle);
        GL.DeleteShader(vertexHandle);
        GL.DeleteShader(fragmentHandle);
    }

    private void CheckCompileErrors(int shader, string type)
    {
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
        {
            string infoLog = GL.GetShaderInfoLog(shader);
            Console.WriteLine($"ERROR::SHADER_COMPILATION_ERROR of type: {type}\n{infoLog}\n");
        }
    }

    private void CheckLinkErrors(int program)
    {
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int success);
        if (success == 0)
        {
            string infoLog = GL.GetProgramInfoLog(program);
            Console.WriteLine($"ERROR::PROGRAM_LINKING_ERROR\n{infoLog}\n");
        }
    }
}