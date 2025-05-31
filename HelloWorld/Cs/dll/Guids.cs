using System;

namespace HelloWorld
{
    static class Guids
    {
        public static readonly Guid squirrelSymbolProviderGuid = new Guid("add54240-e4f4-4b64-8744-10df664c2678");
        public static readonly Guid squirrelCompilerGuid = new Guid("518ed8bb-e75e-4ffb-b3e2-41fd432bde3f");
        public static readonly Guid squirrelLanguageGuid = new Guid("3fb3b121-93d1-4556-990a-82c64eb2feb9");
        public static readonly Guid squirrelRuntimeGuid = new Guid("27cda8b6-3737-41fe-bbce-90ee772dee73");
    }
    static class MessageToRemote
    {
        public static readonly Guid guid = new Guid("95618bfb-241c-418f-b274-5dbbf6b6b2b4");

        public static readonly int createRuntime = 1;
    }

    static class MessageToLocal
    {
        public static readonly Guid guid = new Guid("6c541a19-bc18-424d-aa11-55fd6c340402");
    }
}