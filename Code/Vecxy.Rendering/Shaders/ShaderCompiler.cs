using Silk.NET.OpenGL;
using Vecxy.Assets;

namespace Vecxy.Rendering;

public sealed class ShaderCompiler(GraphicsDevice device)
{
    private const string FallbackVertexSource =
        """
        #version 330 core

        layout(location = 0) in vec2 aPosition;

        uniform mat4 uTransform;

        void main()
        {
            gl_Position = uTransform * vec4(aPosition, 0.0, 1.0);
        }
        """;

    private const string FallbackFragmentSource =
        """
        #version 330 core

        out vec4 oColor;

        void main()
        {
            oColor = vec4(1.0, 0.0, 1.0, 1.0);
        }
        """;

    public Shader Compile(ShaderAsset asset, string name)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var program = BuildProgram(
            name,
            asset.Vertex.Content,
            asset.Fragment.Content);

        return new Shader(device, program, name);
    }

    public Shader CompileFallback()
    {
        const string name = "Built-in error shader";
        var program = BuildProgram(
            name,
            FallbackVertexSource,
            FallbackFragmentSource);

        return new Shader(device, program, name);
    }

    private uint BuildProgram(string name, string vertexSource, string fragmentSource)
    {
        var gl = device.GL;
        var vertex = CompileStage(name, ShaderType.VertexShader, vertexSource);
        uint fragment = 0;
        uint program = 0;

        try
        {
            fragment = CompileStage(name, ShaderType.FragmentShader, fragmentSource);
            program = gl.CreateProgram();
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out var linked);

            if (linked == 0)
            {
                throw new InvalidOperationException(
                    $"Shader '{name}' linking failed:{Environment.NewLine}{gl.GetProgramInfoLog(program)}");
            }

            return program;
        }
        catch
        {
            if (program != 0)
            {
                gl.DeleteProgram(program);
            }

            throw;
        }
        finally
        {
            gl.DeleteShader(vertex);
            if (fragment != 0)
            {
                gl.DeleteShader(fragment);
            }
        }
    }

    private uint CompileStage(string name, ShaderType type, string source)
    {
#if ANDROID
        source = ConvertToOpenGles(source, type);
#endif
        var gl = device.GL;
        var shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out var compiled);

        if (compiled != 0)
        {
            return shader;
        }

        var log = gl.GetShaderInfoLog(shader);
        gl.DeleteShader(shader);
        throw new InvalidOperationException(
            $"Shader '{name}' {type} compilation failed:{Environment.NewLine}{log}");
    }

#if ANDROID
    private static string ConvertToOpenGles(string source, ShaderType type)
    {
        source = source.Replace(
            "#version 330 core",
            "#version 300 es",
            StringComparison.Ordinal);

        if (type == ShaderType.FragmentShader &&
            !source.Contains("precision ", StringComparison.Ordinal))
        {
            const string version = "#version 300 es";
            source = source.Replace(
                version,
                version + "\nprecision highp float;\nprecision highp int;",
                StringComparison.Ordinal);
        }

        return source;
    }
#endif
}
