using System.ComponentModel;

namespace Application.Types
{
    public enum AppStoredFileStatusTypes
    {
        [Description("Error during upload")]
        Error = 1,

        [Description("Uploading")]
        Uploading = 2,

        [Description("Upload complete")]
        Complete = 3
    }
}
