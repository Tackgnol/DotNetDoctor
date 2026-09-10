using System;

namespace ScopeTest;

public sealed class FakeTests
{
    public void Rethrow()
    {
        try
        {
            Work();
        }
        catch (Exception ex)
        {
            throw ex;
        }
    }

    private static void Work()
    {
    }
}
