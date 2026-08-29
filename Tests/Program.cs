var result = CoreRegressionTests.Run();
NpcCompendiumWorkbookPackageTests.Run(result);
Console.WriteLine($"Core regression tests: {result.Passed} passed, {result.Failed} failed.");
for (var i = 0; i < result.Failures.Count; i++)
    Console.WriteLine("FAIL: " + result.Failures[i]);
return result.Success ? 0 : 1;
