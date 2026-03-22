using System;
using System.Text;

namespace Ambi.Utils;

public static class DataFormat
{
    private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("hw_save_key2026");

    /// <summary>
    /// Шифрует строку с использованием XOR и контрольной суммы
    /// </summary>
    public static string Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var result = string.Empty;
        var data = Encoding.UTF8.GetBytes(text);

        for (int i = 0; i < data.Length; i++)
            data[i] ^= EncryptionKey[i % EncryptionKey.Length];

        var originalBytes = Encoding.UTF8.GetBytes(text);
        byte checksum = 0;
        foreach (var b in originalBytes)
            checksum ^= b;

        // Добавляем зашифрованную контрольную сумму в начало
        var newChecksum = new byte[data.Length + 1];
        newChecksum[0] = (byte)(checksum ^ EncryptionKey[0]);
        Array.Copy(data, 0, newChecksum, 1, data.Length);

        result = Convert.ToBase64String(newChecksum);

        return result;
    }

    /// <summary>
    /// Расшифровывает строку
    /// </summary>
    public static string Decode(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return string.Empty;

        try
        {
            var withChecksum = Convert.FromBase64String(encrypted);
            if (withChecksum.Length < 2)
                return null;

            // Извлекаем контрольную сумму
            byte storedChecksum = (byte)(withChecksum[0] ^ EncryptionKey[0]);

            // Извлекаем и расшифровываем данные
            var data = new byte[withChecksum.Length - 1];
            Array.Copy(withChecksum, 1, data, 0, data.Length);

            for (int i = 0; i < data.Length; i++)
                data[i] ^= EncryptionKey[i % EncryptionKey.Length];

            var result = Encoding.UTF8.GetString(data);

            // Проверяем контрольную сумму
            var bytes = Encoding.UTF8.GetBytes(result);
            byte calculatedChecksum = 0;
            foreach (var b in bytes)
                calculatedChecksum ^= b;

            if (calculatedChecksum != storedChecksum)
            {
                Log.Warning("DataFormat: Контрольная сумма не совпадает, данные могут быть повреждены");
                return null;
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error($"[DataFormat] {ex.Message}");

            return null;
        }
    }
}