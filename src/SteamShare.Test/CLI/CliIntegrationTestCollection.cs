using Xunit;

namespace SteamShare.Test.CLI;

/// <summary>
/// 强制 CliIntegrationTests 和 CliEndToEndTests 串行执行，避免 dotnet run MSBuild 竞争和预置数据冲突。
/// </summary>
[CollectionDefinition("SequentialCliTests", DisableParallelization = true)]
public class SequentialCliTestCollection
{
}
