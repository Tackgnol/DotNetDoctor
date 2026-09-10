using System;

namespace NoWarnLib;

public sealed class Suppressed
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
