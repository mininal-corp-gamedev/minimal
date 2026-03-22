using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Ambi.Utils;

public static class DataStore
{
    /// <summary>
    /// Сохраняет данные в зашифрованный файл
    /// </summary>
    public static void Save<T>(T data, string fileName)
    {
        try
        {
            // Сериализуем объект в JSON
            string jsonData = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = false
            });

            // Шифруем данные
            string encryptedData = DataFormat.Encode(jsonData);

            // Сохраняем через FileSystem API s&box
            FileSystem.Data.WriteAllText($"{fileName}.dat", encryptedData);

            Log.Info($"[DataStore] Save {fileName}.dat");
        }
        catch (Exception ex)
        {
            Log.Error($"[DataStore] Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Загружает данные из зашифрованного файла
    /// </summary>
    public static T Load<T>(string fileName) where T : new()
    {
        try
        {
            string filePath = $"{fileName}.dat";

            // Проверяем существование файла
            if (!FileSystem.Data.FileExists(filePath))
            {
                Log.Warning($"Файл сохранения не найден: {filePath}");
                return new T();
            }

            // Читаем зашифрованные данные
            string encryptedData = FileSystem.Data.ReadAllText(filePath);

            // Расшифровываем данные
            string jsonData = DataFormat.Decode(encryptedData);

            if (string.IsNullOrEmpty(jsonData))
            {
                Log.Error("Не удалось расшифровать данные");
                return new T();
            }

            // Десериализуем JSON в объект
            T result = JsonSerializer.Deserialize<T>(jsonData);

            Log.Info($"Данные успешно загружены: {filePath}");
            return result ?? new T();
        }
        catch (Exception ex)
        {
            Log.Error($"DataStore.Load: {ex.Message}");
            return new T();
        }
    }

    /// <summary>
    /// Проверяет существование файла сохранения
    /// </summary>
    public static bool Exists(string fileName)
    {
        return FileSystem.Data.FileExists($"{fileName}.dat");
    }

    /// <summary>
    /// Удаляет файл сохранения
    /// </summary>
    public static void Delete(string fileName)
    {
        try
        {
            string filePath = $"{fileName}.dat";
            if (FileSystem.Data.FileExists(filePath))
            {
                FileSystem.Data.DeleteFile(filePath);
                Log.Info($"Файл сохранения удалён: {filePath}");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"DataStore.Delete: {ex.Message}");
        }
    }

    /// <summary>
    /// Получает список всех файлов сохранений
    /// </summary>
    public static string[] GetAllSaves()
    {
        try
        {
            var files = FileSystem.Data.FindFile("", "*.dat");
            return files.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error($"DataStore.GetAllSaves: {ex.Message}");
            return Array.Empty<string>();
        }
    }
}
