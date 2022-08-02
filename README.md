# WritableJsonConfiguration

<img src="https://github.com/Kibnet/WritableJsonConfiguration/raw/master/resources/JSON_logo.png" alt="JSON" width="180"/>

![](https://github.com/Kibnet/WritableJsonConfiguration/workflows/NuGet%20Generation/badge.svg?branch=master)
![](https://img.shields.io/github/issues/Kibnet/WritableJsonConfiguration.svg?label=Issues)
![](https://img.shields.io/github/tag/Kibnet/WritableJsonConfiguration.svg?label=Last%20Version)
![GitHub last commit](https://img.shields.io/github/last-commit/kibnet/WritableJsonConfiguration)
![GitHub code size in bytes](https://img.shields.io/github/languages/code-size/kibnet/WritableJsonConfiguration?label=Code%20Size)

![GitHub search hit counter](https://img.shields.io/github/search/kibnet/WritableJsonConfiguration/SignalR?label=GitHub%20Search%20Hits)
![Nuget](https://img.shields.io/nuget/dt/WritableJsonConfiguration?label=Nuget%20Downloads)

## What is it?
If you want to add a settings to your app and you like json, then this is what you need. This uses the standard interface for working with settings, which is provided by Microsoft.Extensions.Configuration. To do this, we added methods for changing values in settings via the standard interface, which will edit the json file themselves.

## How to use?
### Add [Nuget Package](https://www.nuget.org/packages/WritableJsonConfiguration/) in your project:
```
Install-Package WritableJsonConfiguration
```
### Create configuration:
```csharp
IConfigurationRoot configuration = WritableJsonConfigurationFabric.Create("Settings.json");
```
### Use this configuration in the app as you need, usually people register it with the IoC, example([Splat](https://github.com/reactiveui/splat)):
```csharp
Locator.CurrentMutable.RegisterConstant(configuration, typeof(IConfiguration));
```
### Get value:
```csharp
Themes theme = configuration.GetSection("Appearance:Theme").Get<Themes>();
```
or
```csharp
Themes theme = configuration.Get<Themes>("Appearance:Theme");
```
### Set value:
```csharp
configuration.GetSection("Appearance:Theme").Set(theme);
```
or
```csharp
configuration.Set("Appearance:Theme", theme);
```

## Communication
Any suggestions and comments are welcome. If you want to contact me, use [Telegram](https://t.me/kibnet)
