using System.Globalization;
using System.Numerics;

namespace Vecxy.Rendering;

// Docs: https://en.wikipedia.org/wiki/Wavefront_.obj_file
// https://www.youtube.com/watch?v=iClme2zsg3I
// https://en.wikipedia.org/wiki/Non-uniform_rational_B-spline
// https://en.wikipedia.org/wiki/Shading
// https://docs.blender.org/manual/en/latest/files/import_export/obj.html#
// https://gist.github.com/warmwaffles/961bed66d9c1d2ecf30c
// 

public class Obj(string source)
{
    public string Source { get; set; } = source;
    public List<string> Comments { get; } = [];
    public List<ObjObject> Objects { get; } = [];

    public string[] GetLines()
    {
        return Source.Split('\n');
    }
}

public class ObjObject(string name)
{
    public string Name { get; set; } = name;
    public bool IsSmoothShading { get; set; }
    public List<Vertex> Vertices { get; } = [];
}

public enum OBJ_TAG : byte
{
    COMMENT = 1,

    NAME = 2,

    VERTEX = 3,
    NORMAL = 4,
    UV = 5,

    SMOOTH_SHADING = 6,
    FACE = 7

    // TODO: L (l v1 v2 v3 v4 v5 v6 ...) 
}

public class ObjParser
{
    private struct ObjLine
    {
        public OBJ_TAG Tag;
        public List<string> Data;

        public string ToText()
        {
            return string.Join(" ", Data);
        }

        public bool ToBool()
        {
            return Data[0] == "1";
        }
    }

    private static readonly Dictionary<string, OBJ_TAG> TAGS = new()
    {
        { "#", OBJ_TAG.COMMENT },

        { "o", OBJ_TAG.NAME },

        { "v", OBJ_TAG.VERTEX },
        { "vn", OBJ_TAG.NORMAL },
        { "vt", OBJ_TAG.UV },

        { "s", OBJ_TAG.SMOOTH_SHADING },
        { "f", OBJ_TAG.FACE },
    };

    private string[] _lines;
    private int _position;
    private Obj _obj;

    private ObjLine? _currentLine;

    private readonly List<Vector3> _v = [];
    private readonly List<Vector3> _vn = [];
    private readonly List<Vector2> _vt = [];

    public Obj Parse(string source)
    {
        _obj = new Obj(source.Trim());
        
        _lines = _obj.GetLines();
        _position = 0;
        
        while (_position < _lines.Length)
        {
            var line = GetCurrentLine();

            switch (line.Tag)
            {
                case OBJ_TAG.COMMENT:
                {
                    var comment = line.ToText();

                    _obj.Comments.Add(comment);

                    Next();
                    continue;
                }

                case OBJ_TAG.NAME:
                {
                    var objObject = ParseObject();
                    _obj.Objects.Add(objObject);
                    continue;
                }

                default:
                    throw new KeyNotFoundException($"Parse. Not a valid obj tag: {line.Tag}");
            }
        }

        return _obj;
    }

    private ObjObject ParseObject()
    {
        var line = GetCurrentLine();

        if (line.Tag != OBJ_TAG.NAME)
        {
            throw new KeyNotFoundException($"ParseObject. Not a valid obj tag: {line.Tag}");
        }
        
        var objObject = new ObjObject(line.ToText());

        Next();

        do
        {
            line = GetCurrentLine();

            switch (line.Tag)
            {
                case OBJ_TAG.VERTEX:
                {
                    var x = float.Parse(line.Data[0], CultureInfo.InvariantCulture);
                    var y = float.Parse(line.Data[1], CultureInfo.InvariantCulture);
                    var z = float.Parse(line.Data[2], CultureInfo.InvariantCulture);

                    var position = new Vector3(x, y, z);

                    _v.Add(position);

                    Next();
                    continue;
                }

                case OBJ_TAG.NORMAL:
                {
                    var x = float.Parse(line.Data[0], CultureInfo.InvariantCulture);
                    var y = float.Parse(line.Data[1], CultureInfo.InvariantCulture);
                    var z = float.Parse(line.Data[2], CultureInfo.InvariantCulture);

                    var direction = new Vector3(x, y, z);

                    _vn.Add(direction);

                    Next();
                    continue;
                }

                case OBJ_TAG.UV:
                {
                    var x = float.Parse(line.Data[0], CultureInfo.InvariantCulture);
                    var y = float.Parse(line.Data[1], CultureInfo.InvariantCulture);

                    var position = new Vector2(x, y);

                    _vt.Add(position);

                    Next();
                    break;
                }

                case OBJ_TAG.SMOOTH_SHADING:
                {
                    objObject.IsSmoothShading = line.ToBool();

                    Next();
                    continue;
                }

                case OBJ_TAG.FACE:
                {
                    // Supported: 'Vertex normal indices' (f v1/vt1/vn1 v2/vt2/vn2 v3/vt3/vn3 ...)
                    // TODO: Vertex texture coordinate indices (f v1/vt1 v2/vt2 v3/vt3 ...)
                    // TODO: Vertex normal indices without texture coordinate indices (f v1//vn1 v2//vn2 v3//vn3 ...)
                    var vertices = line.Data;

                    if (vertices.Count > 3)
                    {
                        // TODO: Create 'Triangulator'
                        // https://en.m.wikipedia.org/wiki/Triangle_mesh
                        // https://github.com/wo80/Triangle.NET/wiki/Mesh
                        throw new NotSupportedException("Faces are not supported more than 3 vertices.");
                    }

                    foreach (var vertexRaw in vertices)
                    {
                        // v1/vt1/vn1
                        var indexes = vertexRaw.Split("/");
                        
                        // If an index is positive then it refers to the offset in that vertex list, starting at 1
                        var vIndex = int.Parse(indexes[0]);

                        if (vIndex < 0)
                        {
                            throw new KeyNotFoundException($"ParseObject. Not a valid vertex index: {vIndex}");
                        }

                        vIndex -= 1;
                    
                        // A valid texture coordinate index starts from 1
                        // and matches the corresponding element in the previously defined list of texture coordinates.
                        var vtIndex = int.Parse(indexes[1]) - 1;
                        
                        // TODO: Optionally
                        // A valid normal index starts from 1
                        // and matches the corresponding element in the previously defined list of normals.
                        var vnIndex = int.Parse(indexes[2]) - 1;

                        var vertex = new Vertex
                        {
                            Position = _v[vIndex],
                            Normal = _vn[vnIndex],
                            UV = _vt[vtIndex]
                        };
                    
                        objObject.Vertices.Add(vertex);
                    }
                    
                    Next();
                    continue;
                }

                case OBJ_TAG.NAME:
                {
                    break;
                }

                default:
                    throw new KeyNotFoundException($"Parse. Not a valid obj tag: {line.Tag}");
            }
        } while (_position < _lines.Length && line.Tag != OBJ_TAG.NAME);
        
        return objObject;
    }
    
    private ObjLine GetCurrentLine()
    {
        if (_currentLine.HasValue)
        {
            return _currentLine.Value;
        }

        var line = _lines[_position];

        var buffer = line
            .Split(" ")
            .ToList();

        var key = buffer[0];

        if (TAGS.TryGetValue(key, out var tag) == false)
        {
            throw new Exception($"ObjParser. Unknown tag: {key}");
        }

        buffer.RemoveAt(0);

        _currentLine = new ObjLine
        {
            Tag = tag,
            Data = buffer
        };

        return _currentLine.Value;
    }

    private void Next()
    {
        _currentLine = null;
        _position++;
    }
}