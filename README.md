# VZX FiveM External — port C#

Projeto .NET 8 para Windows x64 que migra o ciclo de conexão, memória remota, offsets, leitura de entidades e veículos, opções e janela de overlay do projeto C++.

## Compilar

```powershell
dotnet build -c Release
```

O executável fica em `bin\Release\net8.0-windows\win-x64` quando publicado com runtime x64, ou em `bin\Release\net8.0-windows` após o build padrão.

## Mapeamento

| C++ | C# |
| --- | --- |
| `Framework/Memory` | `ProcessMemory.cs` |
| `Cheat/SDK/Game` | `GameSdk.cs` e `Offsets.cs` |
| `Cheat/SDK/CPed` | `Entity.cs` |
| `Cheat/Features` | `CheatController.cs` |
| `Framework/Overlay` | `OverlayForm.cs` |
| `Globals` | `Settings.cs` |

O código seleciona b3258 quando o nome do módulo contém `3258`; nos demais casos usa b3095, como o original. Antes de usar em qualquer build diferente, atualize e valide os offsets; eles mudam entre versões do FiveM.
