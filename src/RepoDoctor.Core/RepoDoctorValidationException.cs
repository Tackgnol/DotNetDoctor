namespace RepoDoctor.Core;

public sealed class RepoDoctorValidationException : Exception
{
    public RepoDoctorValidationException(string message)
        : base(message)
    {
    }

    public RepoDoctorValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
