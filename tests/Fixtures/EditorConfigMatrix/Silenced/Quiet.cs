using System;

namespace EditorConfigMatrix.Silenced;

public sealed class Quiet
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
