using System;
using UnityEngine;

// Everything the business card says. Read from Assets/Resources/business_card.json, so the text, skills, links
// and portfolio can be changed by editing that file, with no Unity work and no rebuild. Anything the file
// leaves out keeps the default below.
[Serializable]
public class BusinessCardContent
{
    [Serializable]
    public class Project
    {
        public string title;
        public string caption;
        public string image; // a picture under Assets/Resources, without the extension: "Portfolio/christmas"
    }

    public string name = "Howard Wilyman";
    public string tagline = "Student and AR experience builder";
    public string about = "Hi! I'm Howard, a student at Xiamen University Malaysia. I like turning ideas into interactive experiences, like this AR card. Tap the other nodes to see what I do and how to reach me.";
    public string[] interests = { "Augmented reality", "Game design", "Web apps" };
    public string[] skills = { "Unity", "Vuforia AR", "C#", "3D animation", "UI design", "Web development" };
    public Project[] portfolio = { new Project { title = "AR Christmas Card", caption = "A snowy house that opens like a dollhouse. Built with Unity and Vuforia.", image = "Portfolio/placeholder_1" } };
    public string email = "wilymanhoward@gmail.com";
    public string phone = "014-238 6192";
    public string website = "howardw.web.app";
    public string linkedin = "";
    public string github = "";

    public static BusinessCardContent Load()
    {
        var content = new BusinessCardContent();
        var file = Resources.Load<TextAsset>("business_card");
        if (file) JsonUtility.FromJsonOverwrite(file.text, content);
        else Debug.LogWarning("business_card.json not found in Resources: using the built-in text.");
        return content;
    }
}
