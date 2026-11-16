var q = UnityEngine.QualitySettings.names[UnityEngine.QualitySettings.GetQualityLevel()];
return q + " | levels: " + string.Join(",", UnityEngine.QualitySettings.names)
    + " | playing " + UnityEditor.EditorApplication.isPlaying
    + " | screen " + UnityEngine.Screen.width + "x" + UnityEngine.Screen.height;
