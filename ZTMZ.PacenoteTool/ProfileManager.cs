using System.IO;

namespace ZTMZ.PacenoteTool
{
    public class ProfileManager
    {
        public string CurrentProfile { set; get; }
        public ProfileManager()
        {
            Directory.CreateDirectory(string.Format("profiles"));
        }

        public void CreateNewProfile(string profileName)
        {
            Directory.CreateDirectory(string.Format("profiles/{0}", profileName));
            this.CurrentProfile = profileName;
        }

        public string StartRecording(string itinerary)
        {
            string filesPath = string.Format("profiles/{0}/{1}", this.CurrentProfile, itinerary);
            Directory.CreateDirectory(filesPath);
            return filesPath;
        }
    }
}