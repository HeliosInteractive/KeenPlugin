# Keen IO Unity3D plugin

Hi! This is the repository of Helios' Keen IO plugin. This is a write-only plugin written specifically for Unity3D with caching support (no Keen administration/read operations. solely one-way metric writing to Keen servers).

Caching feature can come in handy if you are using Keen IO in situations where Internet connectivity is sparse. This library caches your calls and sends them at a later time when connection is back online and stable.

## Build notes

Remember to change your runtime configuration from **.NET subset to .NET** in build settings before building your Unity3D project (not needed if you are not using the SQLite cache provider).

Binaries for SQLite on Windows are checked in (x86 and x86_64). You may obtain additional binaries for other platforms from [SQLite project's website](http://www.sqlite.org/) if you need to.

## Sample usage

You may begin by importing the latest stable release `.unitypackage`. There is an example scene and an example script (`MetricsExample.cs`) in this repository which shows you how to use this plugin.

----

Start by having an instance of `Helios.Keen.Client` which is a `MonoBehaviour`:

```C#
var MetricsClient = gameObject.AddComponent<Helios.Keen.Client>();
```

Then provide it your project's specific settings:

```C#
MetricsClient.Settings = new Helios.Keen.Client.Config
{
	/* [REQUIRED] Keen.IO project id, Get this from Keen dashboard */
	ProjectId           = "none",
	/* [REQUIRED] Keen.IO write key, Get this from Keen dashboard */
	WriteKey            = "none",
	/* [OPTIONAL] Attempt to sweep the cache every 45 seconds */
	CacheSweepInterval  = 45.0f,
	/* [OPTIONAL] In every sweep attempt pop 10 cache entries */
	CacheSweepCount     = 10,
	/* [OPTIONAL] This is the callback per Client's event emission */
	EventCallback       = OnKeenClientEvent,
	/* [OPTIONAL] If not provided, cache is turned off */
	CacheInstance       = new Helios.Keen.Cache.Sqlite("path/to/db")
};
```

And start sending events:

```C#
// This is an example of sending Helios specific events
MetricsClient.SendQuizEvent(new Helios.Keen.Client.QuizEvent
{
	quizId = "IQ test",
	quizResult = "failed",
	experienceData  = new Helios.Keen.Client.ExperienceData
	{
		experienceLabel = "Keen Plugin",
		versionNumber   = "1.0.0",
		location        = "never land"
	}
});

// This is an example of using custom data types
MetricsClient.SendEvent("custom_event", new CustomData
{
	data_member_1 = "test string",
	data_member_2 = 25000.0f,
	data_member_3 = new CustomNestedData
	{
		data_member_1 = "\"nested\" string",
		data_member_2 = 25000d,
	}
});
```

Don't forget to cleanup after yourself! (In case you used `AddComponent`)

```C#
Destroy(MetricsClient);
```

Take a look at `MetricsExample.cs` for more in-depth usage examples.
Also `SessionExample.cs` shows you how to use `StateAwareClient` class.

## Built-in JSON serializer notes

There is an extremely simplistic JSON serializer built into this library (about 60 lines of code with comments!) which provides a portable and backwards compatible C# implementation for serializing **FLAT CLASSES, FLAT STRUCTS, and POD data types**.

This means the serializer *does not support fancy features such as inheritance*. You can absolutely use a custom and more advanced JSON serializer; if you absolutely need to work with more complicated data types. Here's an example of using [Unity 5.3's JSON serializer](http://docs.unity3d.com/Manual/JSONSerialization.html) and using the `SendEvent(string, string)` overload:

```C#
MetricsClient.SendSession("eventName", JsonUtility.ToJson(myComplexObject));
```

## Source notes

There are two important namespaces:

 1. `Helios.Keen`
 2. `Helios.Keen.Cache`

All cache implementations go under `Helios.Keen.Cache` namespace. For now it only contains a SQLite cache implementation. This is an optional dependency. You are more than welcome to strip it out and provide a custom `ICacheProvider` implementation of your own.

SQLite cache provider has a dependency on native `sqlite3` binaries. They are checked in under `Plugins` folder for Windows 32/64 bit and Android x86/armabi-v7. *Note that SQLite provider is optional. You can provide your own caching mechanism*.

class `Helios.Keen.Client` is the actual client library. class `Helios.Keen.Cache.Sqlite` is the SQLite cache provider to be used optionally with the client instance.

## SQLite cache implementation notes

For optimal performance I highly recommend multiple instances of `Helios.Keen.Client` in conjunction with `Helios.Keen.Cache.Sqlite` instances. Each instance of `Helios.Keen.Cache.Sqlite` must point to a separate database for optimal performance otherwise it causes race conditions/lock issues between multiple instances pointing to the same database.

You may also consider installing [Visual C++ Redistributable for Visual Studio 2015](https://www.microsoft.com/en-us/download/details.aspx?id=48145) if you are using binaries provided in this repository under Windows.

## Helios extensions

Helios internally uses some conventions when dealing with Keen.IO. These conventions can be found in `Helios/Keen/ClientHeliosExtension.cs`.
