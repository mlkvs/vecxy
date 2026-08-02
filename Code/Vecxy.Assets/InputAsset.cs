using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vecxy.Assets;


public sealed class InputAsset
{
    public string Namespace { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public List<InputMapAsset> Maps { get; set; } = [];
}

public sealed class InputMapAsset
{
    public string Name { get; set; } = string.Empty;
    public List<InputActionAsset> Actions { get; set; } = [];
}

public sealed class InputActionAsset
{
    public string Name { get; set; } = string.Empty;
    public EInputActionType Type { get; set; }
    public List<InputBindingAsset> Bindings { get; set; } = [];
}

public sealed class InputBindingAsset
{
    public EInputBindingType Type { get; set; }

    public EKeyboardKey Key { get; set; }

    public EMouseButton Mouse { get; set; }

    public string Composite { get; set; } = string.Empty;
}

public enum EInputActionType : byte
{
    Undefined,
    Button,
    Axis,
    Vector2,
}

public enum EInputBindingType : byte
{
    Undefined,
    Keyboard,
    Mouse,
    Composite,
}

public enum EKeyboardKey : int
{
    Undefined,
    Space = 32,
    Apostrophe = 39,
    Comma = 44,
    Minus = 45,
    Period = 46,
    Slash = 47,
    Number0 = 48,
    Number1 = 49,
    Number2 = 50,
    Number3 = 51,
    Number4 = 52,
    Number5 = 53,
    Number6 = 54,
    Number7 = 55,
    Number8 = 56,
    Number9 = 57,
    Semicolon = 59,
    Equal = 61,
    A = 65,
    B = 66,
    C = 67,
    D = 68,
    E = 69,
    F = 70,
    G = 71,
    H = 72,
    I = 73,
    J = 74,
    K = 75,
    L = 76,
    M = 77,
    N = 78,
    O = 79,
    P = 80,
    Q = 81,
    R = 82,
    S = 83,
    T = 84,
    U = 85,
    V = 86,
    W,
    X = 88,
    Y = 89,
    Z = 90,
    LeftBracket = 91,
    BackSlash = 92,
    RightBracket = 93,
    GraveAccent = 96,
    World1 = 161,
    World2 = 162,
    Escape = 256,
    Enter = 257,
    Tab = 258,
    Backspace = 259,
    Insert = 260,
    Delete = 261,
    Right = 262,
    Left = 263,
    Down = 264,
    Up = 265,
    PageUp = 266,
    PageDown = 267,
    Home = 268,
    End = 269,
    CapsLock = 280,
    ScrollLock = 281,
    NumLock = 282,
    PrintScreen = 283,
    Pause = 284,
    F1 = 290,
    F2 = 291,
    F3 = 292,
    F4 = 293,
    F5 = 294,
    F6 = 295,
    F7 = 296,
    F8 = 297,
    F9 = 298,
    F10 = 299,
    F11 = 300,
    F12 = 301,
    F13 = 302,
    F14 = 303,
    F15 = 304,
    F16 = 305,
    F17 = 306,
    F18 = 307,
    F19 = 308,
    F20 = 309,
    F21 = 310,
    F22 = 311,
    F23 = 312,
    F24 = 313,
    F25 = 314,
    Keypad0 = 320,
    Keypad1 = 321,
    Keypad2 = 322,
    Keypad3 = 323,
    Keypad4 = 324,
    Keypad5 = 325,
    Keypad6 = 326,
    Keypad7 = 327,
    Keypad8 = 328,
    Keypad9 = 329,
    KeypadDecimal = 330,
    KeypadDivide = 331,
    KeypadMultiply = 332,
    KeypadSubtract = 333,
    KeypadAdd = 334,
    KeypadEnter = 335,
    KeypadEqual = 336,
    LeftShift = 340,
    LeftControl = 341,
    LeftAlt = 342,
    LeftSuper = 343,
    RightShift = 344,
    RightControl = 345,
    RightAlt = 346,
    RightSuper = 347,
    Menu = 348,
}

public enum EMouseButton : int
{
    Undefined = -1,
    Left = 0,
    Right = 1,
    Middle = 2,
    Button4 = 3,
    Button5 = 4,
    Button6 = 5,
    Button7 = 6,
    Button8 = 7,
}

public static class InputAssetReader
{
    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    public static InputAsset ReadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ReadFromText(File.ReadAllText(path), path);
    }

    public static InputAsset ReadFromText(string source, string path = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(source);

        var asset = Deserializer.Deserialize<InputAsset>(source)
            ?? throw new InvalidDataException(
                $"Input asset is empty: {path}");

        Validate(asset, path);
        return asset;
    }

    public static void Validate(InputAsset asset, string path)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (string.IsNullOrWhiteSpace(asset.Namespace))
        {
            throw new InvalidDataException(
                $"Input asset namespace is empty: {path}");
        }

        if (string.IsNullOrWhiteSpace(asset.ClassName))
        {
            throw new InvalidDataException(
                $"Input asset class name is empty: {path}");
        }

        foreach (var map in asset.Maps)
        {
            if (string.IsNullOrWhiteSpace(map.Name))
            {
                throw new InvalidDataException(
                    $"Input map name is empty: {path}");
            }

            foreach (var action in map.Actions)
            {
                if (string.IsNullOrWhiteSpace(action.Name))
                {
                    throw new InvalidDataException(
                        $"Input action name is empty in map '{map.Name}': {path}");
                }

                if (action.Type == EInputActionType.Undefined)
                {
                    throw new InvalidDataException(
                        $"Input action '{map.Name}.{action.Name}' has undefined type: {path}");
                }

                ValidateBindings(map, action, path);
            }
        }
    }

    private static void ValidateBindings(
        InputMapAsset map,
        InputActionAsset action,
        string path)
    {
        ValidateActionType(map, action, path);

        foreach (var binding in action.Bindings)
        {
            switch (binding.Type)
            {
                case EInputBindingType.Keyboard:
                    if (binding.Key == EKeyboardKey.Undefined)
                    {
                        throw new InvalidDataException(
                            $"Keyboard binding for '{map.Name}.{action.Name}' has no key: {path}");
                    }

                    break;

                case EInputBindingType.Mouse:
                    if (binding.Mouse == EMouseButton.Undefined)
                    {
                        throw new InvalidDataException(
                            $"Mouse binding for '{map.Name}.{action.Name}' has no button: {path}");
                    }

                    break;

                case EInputBindingType.Composite:
                    if (string.IsNullOrWhiteSpace(binding.Composite))
                    {
                        throw new InvalidDataException(
                            $"Composite binding for '{map.Name}.{action.Name}' has no composite type: {path}");
                    }

                    ValidateComposite(map, action, binding, path);

                    break;

                default:
                    throw new InvalidDataException(
                        $"Binding for '{map.Name}.{action.Name}' has undefined type: {path}");
            }
        }
    }

    private static void ValidateActionType(
        InputMapAsset map,
        InputActionAsset action,
        string path)
    {
        switch (action.Type)
        {
            case EInputActionType.Button:
            case EInputActionType.Vector2:
                return;

            default:
                throw new InvalidDataException(
                    $"Input action '{map.Name}.{action.Name}' uses unsupported type '{action.Type}': {path}");
        }
    }

    private static void ValidateComposite(
        InputMapAsset map,
        InputActionAsset action,
        InputBindingAsset binding,
        string path)
    {
        switch (binding.Composite)
        {
            case "WASD":
                return;

            default:
                throw new InvalidDataException(
                    $"Input composite '{binding.Composite}' for '{map.Name}.{action.Name}' is not supported: {path}");
        }
    }
}

public sealed class InputAssetImporter : IAssetImporter<InputAsset>
{
    public IReadOnlyCollection<string> Extensions { get; } = [".input"];

    public InputAsset Import(
        AssetMetadata metadata,
        AssetImportContext context)
    {
        return InputAssetReader.ReadFromText(
            context.ReadAllText(metadata.Path),
            metadata.Path);
    }
}
