using System;
using Mono.Cecil;

class Program
{
    static void Main()
    {
        var asm = AssemblyDefinition.ReadAssembly("/Volumes/Ext/dev/ressources/dalamud/Hooks/dev/FFXIVClientStructs.dll");
        var type = asm.MainModule.GetType("FFXIVClientStructs.FFXIV.Client.UI.AddonNamePlate/NamePlateObject");
        foreach (var field in type.Fields)
        {
            Console.WriteLine($"FIELD {field.Name}: {field.FieldType}");
        }
    }
}
