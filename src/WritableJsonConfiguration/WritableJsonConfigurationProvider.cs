using System;
using System.IO;
using Microsoft.Extensions.Configuration.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WritableJsonConfiguration
{
    public class WritableJsonConfigurationProvider : JsonConfigurationProvider
    {
        // Конструктор класса, наследуемого от JsonConfigurationProvider
        public WritableJsonConfigurationProvider(JsonConfigurationSource source) : base(source)
        {
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
        private void SetValue(string key, string value, dynamic jsonObj)
        {
            // Вызов базового метода Set для установки значения
            base.Set(key, value);
            // Разделение ключа на части для навигации по структуре JSON
            var split = key.Split(':');
            var context = jsonObj;
            for (int i = 0; i < split.Length; i++)
            {
                var currentKey = split[i];
                if (i < split.Length - 1) // Если не последний элемент пути, обрабатываем вложенные объекты или массивы
                {
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
            var jsonObj = GetJsonObj(); // Получаем текущий JSON-объект
            SetValue(key, value, jsonObj); // Устанавливаем значение
            Save(jsonObj); // Сохраняем изменения в файл
        }

        // Перегрузка метода Set для установки значения любого типа
        public void Set(string key, object value)
        {
            var jsonObj = GetJsonObj(); // Получаем текущий JSON-объект
            var serialized = JsonConvert.SerializeObject(value); // Сериализуем значение
            var jToken = JsonConvert.DeserializeObject(serialized) as JToken ?? new JValue(value); // Преобразуем сериализованное значение в JToken
            WalkAndSet(key, jToken, jsonObj); // Рекурсивно устанавливаем значение в JSON-объект
            Save(jsonObj); // Сохраняем изменения в файл
        }

        // Рекурсивный метод для установки значения в JSON-объект
        private void WalkAndSet(string key, JToken value, dynamic jsonObj)
        {
            switch (value)
            {
                case JArray jArray: // Обработка массива значений
                    {
                        for (int index = 0; index < jArray.Count; index++)
                        {
                            var currentKey = $"{key}:{index}"; // Генерация ключа для элемента массива
                            var elementValue = jArray[index]; // Получение элемента массива
                            WalkAndSet(currentKey, elementValue, jsonObj); // Рекурсивный вызов для установки значения элемента
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
                            WalkAndSet(currentKey, propValue, jsonObj); // Рекурсивный вызов для установки значения свойства
                        }
                        break;
                    }
                case JValue jValue: // Обработка примитивного значения
                    {
                        SetValue(key, jValue.ToString(), jsonObj); // Установка значения
                        break;
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(value)); // Исключение для необработанных типов данных
            }
        }
    }
}
