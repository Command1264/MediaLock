# Use .NET 10 and WPF with an UI-independent Core

Media Lock targets Windows-native media and input APIs, so it will use C# on .NET 10 LTS and WPF for its desktop
UI. WPF gives the presentation layer mature XAML binding and Windows integration, while keeping Core independent
of WPF preserves testability and leaves open a future UI replacement; the cost is an intentionally Windows-only
application and explicit adapter boundaries around platform APIs.
