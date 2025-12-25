namespace Application.Types
{
    public enum ApplicationLogType
    {
        Code = 1,
        Exception = 2,
        Json = 3,
        Message = 4,
        Query = 5
    }

    public enum ApplicationLogAction
    {
        Error = 1,
        Warning = 2,
        Success = 3,
        Info = 4
    }
}
