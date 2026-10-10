// The Kusto language service is compiled from C# (Bridge.NET) and expects Node's `global` object.
(self as unknown as { global: typeof globalThis }).global = self;
export {};
