using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WritableJsonConfiguration
{
    public class WritableJsonConfigurationProvider : JsonConfigurationProvider
    {
        private static readonly ConcurrentDictionary<string, object> PathLocks =
            new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly bool useAtomicWrites;
        private bool writesBlocked;
        internal Action<AtomicWriteStage, string> AtomicWriteCheckpoint { get; set; }

        // Конструктор класса, наследуемого от JsonConfigurationProvider
        public WritableJsonConfigurationProvider(JsonConfigurationSource source) : base(source)
        {
            useAtomicWrites = (source as WritableJsonConfigurationSource)?.UseAtomicWrites == true;
            if (useAtomicWrites) AtomicSettingsFile.EnsureSupported();
        }

        // Метод для сохранения JSON-объекта в файл
        private void Save(dynamic jsonObj)
        {
            // Получаем полный путь к файлу конфигурации
            var fileFullPath = base.Source.FileProvider.GetFileInfo(base.Source.Path).PhysicalPath;
            // Сериализуем объект в форматированный JSON-строку
            string output = JsonConvert.SerializeObject(jsonObj, Formatting.Indented);
            // Записываем строку в файл, перезаписывая его содержимое
            File.WriteAllText(fileFullPath, output);
        }

        // Установка значения по ключу в JSON-объекте
        private void SetValue(string key, string value, dynamic jsonObj, bool publishData = true)
        {
            // Вызов базового метода Set для установки значения
            if (publishData) base.Set(key, value);
            // Разделение ключа на части для навигации по структуре JSON
            var split = key.Split(':');
            var context = jsonObj;
            for (int i = 0; i < split.Length; i++)
            {
                var currentKey = split[i];
                if (i < split.Length - 1) // Если не последний элемент пути, обрабатываем вложенные объекты или массивы
                {
                    if (!publishData)
                    {
                        // Atomic mode must preserve siblings in the actual parent, not recreate
                        // nested containers by looking for their names at the document root.
                        context = GetOrCreateAtomicChild((JToken)context, currentKey, int.TryParse(split[i + 1], out _));
                        continue;
                    }
                    var child = jsonObj[currentKey];
                    if (child == null) // Если вложенный объект или массив не существует, создаем его
                    {
                        if (i + 1 < split.Length && int.TryParse(split[i + 1], out _))
                        {
                            context[currentKey] = new JArray(); // Создаем массив, если следующий элемент пути - индекс
                        }
                        else
                        {
                            context[currentKey] = new JObject(); // Создаем объект, если следующий элемент пути - ключ
                        }
                    }
                    context = context[currentKey];
                }
                else // Если последний элемент пути, устанавливаем значение
                {
                    if (int.TryParse(currentKey, out var index)) // Обработка индекса массива
                    {
                        if (context is JArray array)
                        {
                            if (array.Count - 1 < index)
                                array.Add(value); // Добавление значения, если индекс выходит за пределы существующего массива
                            else
                                array[index] = value; // Замена значения по индексу, если индекс в пределах массива
                        }
                    }
                    else
                        context[currentKey] = value; // Установка значения для ключа объекта
                }
            }
        }

        // Получение JSON-объекта из файла конфигурации
        private dynamic GetJsonObj()
        {
            // Получаем полный путь к файлу конфигурации
            var fileFullPath = base.Source.FileProvider.GetFileInfo(base.Source.Path).PhysicalPath;
            // Читаем содержимое файла, если файл существует, иначе используем пустой объект
            var json = File.Exists(fileFullPath) ? File.ReadAllText(fileFullPath) : "{}";
            // Десериализуем JSON-строку в объект
            return JsonConvert.DeserializeObject(json);
        }

        // Переопределение метода Set для установки значения по ключу
        public override void Set(string key, string value)
        {
            if (useAtomicWrites)
            {
                SetAtomically(json => SetValue(key, value, json, publishData: false));
                return;
            }
            var jsonObj = GetJsonObj(); // Получаем текущий JSON-объект
            SetValue(key, value, jsonObj); // Устанавливаем значение
            Save(jsonObj); // Сохраняем изменения в файл
        }

        // Перегрузка метода Set для установки значения любого типа
        public void Set(string key, object value)
        {
            if (useAtomicWrites)
            {
                SetAtomically(json =>
                {
                    var token = JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value)) as JToken ?? new JValue(value);
                    WalkAndSet(key, token, json, publishData: false);
                });
                return;
            }
            var jsonObj = GetJsonObj(); // Получаем текущий JSON-объект
            var serialized = JsonConvert.SerializeObject(value); // Сериализуем значение
            var jToken = JsonConvert.DeserializeObject(serialized) as JToken ?? new JValue(value); // Преобразуем сериализованное значение в JToken
            WalkAndSet(key, jToken, jsonObj); // Рекурсивно устанавливаем значение в JSON-объект
            Save(jsonObj); // Сохраняем изменения в файл
        }

        // Рекурсивный метод для установки значения в JSON-объект
        private void WalkAndSet(string key, JToken value, dynamic jsonObj, bool publishData = true)
        {
            switch (value)
            {
                case JArray jArray: // Обработка массива значений
                    {
                        for (int index = 0; index < jArray.Count; index++)
                        {
                            var currentKey = $"{key}:{index}"; // Генерация ключа для элемента массива
                            var elementValue = jArray[index]; // Получение элемента массива
                            WalkAndSet(currentKey, elementValue, jsonObj, publishData); // Рекурсивный вызов для установки значения элемента
                        }
                        break;
                    }
                case JObject jObject: // Обработка объекта
                    {
                        foreach (var propertyInfo in jObject.Properties()) // Перебор свойств объекта
                        {
                            var propName = propertyInfo.Name; // Имя свойства
                            var currentKey = key == null ? propName : $"{key}:{propName}"; // Генерация ключа для свойства
                            var propValue = propertyInfo.Value; // Получение значения свойства
                            WalkAndSet(currentKey, propValue, jsonObj, publishData); // Рекурсивный вызов для установки значения свойства
                        }
                        break;
                    }
                case JValue jValue: // Обработка примитивного значения
                    {
                        SetValue(key, jValue.ToString(), jsonObj, publishData); // Установка значения
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(value)); // Исключение для необработанных типов данных
            }
        }

        private static JToken GetOrCreateAtomicChild(JToken context, string key, bool nextIsArrayIndex)
        {
            if (context is JArray array)
            {
                if (!int.TryParse(key, out var index) || index < 0)
                    throw new ArgumentException("Configuration array path requires a non-negative index.");
                if (index < array.Count) return array[index];
                JToken child = nextIsArrayIndex ? (JToken)new JArray() : new JObject();
                // Preserve the existing append/merge behavior rather than truncating array tails.
                array.Add(child);
                return child;
            }

            var existing = context[key];
            if (existing != null) return existing;
            JToken created = nextIsArrayIndex ? (JToken)new JArray() : new JObject();
            context[key] = created;
            return created;
        }

        private void SetAtomically(Action<JObject> edit)
        {
            try { SetAtomicallyCore(edit); }
            catch (Exception error) when (error is FormatException || error is JsonException)
            {
                // Parser/serializer exceptions can contain settings keys, values or getter errors.
                throw new InvalidDataException("Configuration JSON could not be read or serialized; settings were not saved.");
            }
        }

        private void SetAtomicallyCore(Action<JObject> edit)
        {
            var physicalPath = Source.FileProvider.GetFileInfo(Source.Path).PhysicalPath;
            if (string.IsNullOrEmpty(physicalPath))
                throw new InvalidOperationException("Atomic configuration writes require a physical file path.");
            var path = Path.GetFullPath(physicalPath);
            lock (PathLocks.GetOrAdd(path, _ => new object()))
            {
                if (writesBlocked)
                    throw new IOException("Configuration writes are blocked after an unreconciled I/O failure. Restart the application or recreate the configuration root.");

                string original;
                bool exists;
                try { original = File.ReadAllText(path); exists = true; }
                catch (FileNotFoundException) { original = "{}"; exists = false; }

                var originalData = ParseSnapshot(original);
                var json = JObject.Parse(original);
                var previous = json.DeepClone();
                edit(json);
                if (exists && JToken.DeepEquals(previous, json))
                {
                    Data = originalData;
                    return;
                }
                var serialized = JsonConvert.SerializeObject(json, Formatting.Indented);
                var candidateData = ParseSnapshot(serialized);

                var commitAttempted = false;
                try
                {
                    AtomicSettingsFile.Write(path, serialized, exists, (stage, file) =>
                    {
                        if (stage == AtomicWriteStage.BeforeCommit) commitAttempted = true;
                        AtomicWriteCheckpoint?.Invoke(stage, file);
                    });
                    Data = candidateData;
                }
                catch
                {
                    if (commitAttempted)
                    {
                        // An ambiguous replace failure must not leave a fictitious in-memory snapshot.
                        try { Data = ParseSnapshot(File.ReadAllText(path)); }
                        catch { writesBlocked = true; }
                    }
                    throw;
                }
            }
        }

        private static IDictionary<string, string> ParseSnapshot(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return new SnapshotParser().Parse(stream);
        }

        private sealed class SnapshotParser : JsonConfigurationProvider
        {
            internal SnapshotParser() : base(new JsonConfigurationSource()) { }

            internal IDictionary<string, string> Parse(Stream stream)
            {
                Load(stream);
                return Data;
            }
        }
    }
}
