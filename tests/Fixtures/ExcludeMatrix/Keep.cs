using System;

namespace ExcludeMatrix;

public sealed class Keep
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
