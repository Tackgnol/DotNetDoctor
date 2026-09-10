using System;

namespace EditorConfigMatrix;

public sealed class Reported
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
