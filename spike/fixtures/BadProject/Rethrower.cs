using System;

namespace BadProject;

public sealed class Rethrower
{
    public void RethrowLosesStackTrace_Reported()
    {
        try
        {
            DoWork();
        }
        catch (Exception ex)
        {
            throw ex;
        }
    }

    public void RethrowLosesStackTrace_Suppressed()
    {
#pragma warning disable CA2200
        try
        {
            DoWork();
        }
        catch (Exception ex)
        {
            throw ex;
        }
#pragma warning restore CA2200
    }

    private static void DoWork()
    {
    }
}
