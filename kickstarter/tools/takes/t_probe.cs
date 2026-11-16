var me = Me();
var p = me.transform.position;
var s = School(p); int n1 = schoolSize;
var b = BigSchool(); int n2 = schoolSize;
return "me " + p.ToString("F0") + " fish " + allFish.Count + " | nearest school " + s.ToString("F0") + " n=" + n1 + " dist " + (s - p).magnitude.ToString("F0")
    + " | biggest " + b.ToString("F0") + " n=" + n2 + " ground there " + Ground(b).ToString("F1");
