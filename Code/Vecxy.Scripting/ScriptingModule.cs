using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Vecxy.Kernel;

namespace Vecxy.Scripting;

// 1. КЛАСС ЛОГГЕРА
// Пробрасывает console.log из JS в консоль .NET
public class EngineLogger
{
    public void log(params object[] args)
    {
        Console.WriteLine($"[JS]: {string.Join(" ", args)}");
    }
}

// 2. КЛАСС ХОСТА
// Хранит список всех JS-объектов (MonoBehaviour) и обновляет их
public class ScriptingHost
{
    private readonly List<dynamic> _scripts = new();

    public void registerScriptObject(dynamic script)
    {
        _scripts.Add(script);
        // Console.WriteLine("DEBUG: Скрипт успешно зарегистрирован в C#");
    }

    public void UpdateAll(float dt)
    {
        // Итерируемся с конца, чтобы была возможность безопасно удалять объекты
        for (int i = _scripts.Count - 1; i >= 0; i--)
        {
            try
            {
                _scripts[i].update(dt);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating script at index {i}: {ex.Message}");
            }
        }
    }
}

// 3. ОСНОВНОЙ МОДУЛЬ СКРИПТИНГА
public class ScriptingModule : IModule
{
    private V8ScriptEngine _engine;
    private ScriptingHost _host;
    private string _scriptsPath;
    private string _distPath;
    private bool _initialized = false;

    public void OnInitialize()
    {
        // Настройка путей
        _scriptsPath = "C:\\Users\\melkov\\Desktop\\Vecxy.Scripting.Test";
        _distPath = Path.Combine(_scriptsPath, "dist");

        // 1. Сборка TypeScript (требует установленного Node.js в системе)
        CompileTypeScript();

        // 2. Инициализация движка V8 с поддержкой отладки (порт 9222)
        _engine = new V8ScriptEngine(V8ScriptEngineFlags.EnableDebugging, 9222);

        // --- ВАЖНО: Настройка разрешений для загрузки ES-модулей (ReferenceError fix) ---
        _engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableAllLoading;
        _engine.DocumentSettings.SearchPath = _distPath;

        _host = new ScriptingHost();

        // 3. Добавление объектов в глобальный контекст JS
        _engine.AddHostObject("console", new EngineLogger());
        _engine.AddHostObject("host", _host);

        // 4. Загрузка скомпилированных JS-скриптов
        try
        {
            LoadScripts();
            InstantiateAllScripts();
            _initialized = true;
            Console.WriteLine("[Scripting] Система инициализирована успешно.");
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException != null ? ex.InnerException.Message : "";
            Console.WriteLine($"[Scripting] Ошибка загрузки скриптов: {ex.Message} {detail}");
        }
    }

    private void CompileTypeScript()
    {
        if (!Directory.Exists(_scriptsPath))
        {
            Console.WriteLine($"[Scripting] Ошибка: Директория со скриптами не найдена: {_scriptsPath}");
            return;
        }

        Console.WriteLine("[Scripting] Компиляция TypeScript...");
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/C npm run build",
            WorkingDirectory = _scriptsPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process?.WaitForExit();

        if (process?.ExitCode != 0)
        {
            Console.WriteLine("[Scripting] ВНИМАНИЕ: Ошибка компиляции 'npm run build'. Проверьте наличие tsc.");
        }
    }

    private void LoadScripts()
    {
        if (!Directory.Exists(_distPath))
        {
            Console.WriteLine($"[Scripting] Ошибка: Папка с результатом компиляции не найдена: {_distPath}");
            return;
        }

        // ВАЖНО: Сначала всегда загружаем engine.js (базовый класс), 
        // так как остальные классы будут наследоваться от него.
        var engineJsPath = Path.Combine(_distPath, "engine.js");
        if (File.Exists(engineJsPath))
        {
            ExecuteFile(engineJsPath);
        }

        // Загружаем все остальные скрипты в папке dist
        foreach (var file in Directory.GetFiles(_distPath, "*.js"))
        {
            if (file.EndsWith("engine.js")) continue;
            ExecuteFile(file);
        }
    }

    private void ExecuteFile(string path)
    {
        // Обязательно полный путь к JS файлу в папке dist
        string fullPath = Path.GetFullPath(path);
        string code = File.ReadAllText(fullPath);

        // VS Code сопоставляет брейкпоинты по этому Uri
        var fileUri = new Uri(fullPath);

        var info = new DocumentInfo(fileUri)
        {
            Category = ModuleCategory.Standard
        };

        _engine.Execute(info, code);
    }

    private void InstantiateAllScripts()
    {
        // Получаем все глобальные объекты из JS
        // Мы ищем классы, которые наследуют MonoBehaviour
        _engine.Execute(new DocumentInfo("init_scripts"), @"
        if (globalThis.PendingScripts) {
            for (let scriptClass of globalThis.PendingScripts) {
                console.log(`[System] Создание экземпляра: ${scriptClass.name}`);
                new scriptClass();
            }
        }
    ");
    }

    public void OnTick(float dt)
    {
        // Вызываем обновление всех зарегистрированных JS-объектов
        if (_initialized)
        {
            _host.UpdateAll(dt);
        }
    }

    public void Dispose()
    {
        _engine?.Dispose();
    }
}