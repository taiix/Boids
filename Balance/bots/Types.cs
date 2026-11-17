class Needle {
  public float needle, dir, speed, left, right; public int visit; public int lstate, rstate; // 0 ready 1 hit 2 missed
  public float pressAt = -1; public int pressSide; public bool planned;
  public int hits, misses; public float accSum;
  public static float sweep = 1.0f, box = 0.3f, missPen = 0.08f;  /* applied 2026-11-17, was 0.85 / 0.2 / 0.15 */ const float upFull = 0.35f, minC = 0.2f, perfect = 0.22f, edge = 0.08f, drain = 0.06f;
  float Half => box * 0.5f;
  Random r;
  public Needle(Random r) { this.r = r; dir = r.NextDouble() < .5 ? -1 : 1; left = -Spot(); right = Spot(); }
  float Spot() => minC + Half + (float)r.NextDouble() * (1f - Half - (minC + Half));
  float Rate => 2f / sweep * (1f + upFull * speed);
  int State(int s) => s < 0 ? lstate : rstate;
  void Set(int s, int v) { if (s < 0) lstate = v; else rstate = v; }
  // returns accuracy of a hit this frame (-1 none)
  public float Step(float t, float dt, Skill k, Func<float, float> noise, Func<float> u) {
    float res = -1;
    needle += dir * Rate * dt;
    if (needle > 1) { needle = 2 - needle; dir = -1; }
    if (needle < -1) { needle = -2 - needle; dir = 1; }
    int side = needle > 0 ? 1 : -1;
    if (side != visit) {
      if (visit != 0 && State(visit) == 0) Miss(visit);
      Set(side, 0); visit = side; planned = false;
    }
    // bot: plan one press per side visit, aimed at the box centre on the first pass
    if (!planned && State(side) == 0) {
      planned = true;
      if (u() >= k.lapse) {
        float c = side < 0 ? left : right, x = needle, d = dir, tt = 0;
        for (int i = 0; i < 200; i++) { float px = x; x += d * Rate * dt; if (x > 1) { x = 2 - x; d = -1; } if (x < -1) { x = -2 - x; d = 1; } tt += dt; if ((px - c) * (x - c) <= 0) break; }
        pressAt = t + tt + noise(k.sigma); pressSide = side;
      } else pressAt = -1;
    }
    bool press = pressAt > 0 && t >= pressAt; if (press) pressAt = -1;
    if (u() < k.spurious * dt) press = true;
    if (press) {
      float centre = side < 0 ? left : right, off = Math.Abs(needle - centre);
      if (State(side) == 0 && off <= Half) {
        float acc = 1 - off / Half; speed = Math.Clamp(speed + edge + (perfect - edge) * acc, 0, 1); Set(side, 1);
        float s = Spot(); if (side < 0) right = s; else left = -s; hits++; accSum += acc; res = acc;
      } else Miss(side);
    }
    speed = Math.Clamp(speed - drain * dt, 0, 1);
    return res;
  }
  void Miss(int s) { speed = Math.Clamp(speed - missPen, 0, 1); if (State(s) == 0) Set(s, 2); misses++; }
}


class Biter {
  public float rotation, biteUntil = 0, pressAt = -1; public int weak; public bool planned;
  public const float Spin = 1.5f, BiteSec = 2.1f, Win = 0.3f; const int Strands = 10;
  public int cuts, misses;
  float AngleOf(int i) => rotation + i * MathF.PI * 2 / Strands;
  int NextWeak(float seconds) { int best = 0; float be = 1e9f; for (int i = 0; i < Strands; i++) { float need = Rep(-MathF.PI / 2 - AngleOf(i)); float err = Math.Abs(need / Spin - seconds); if (err < be) { be = err; best = i; } } return best; }
  static float Rep(float a) { float m = 2 * MathF.PI; a %= m; if (a < 0) a += m; return a; }
  public Biter(Random r) { rotation = (float)r.NextDouble() * 6.28f; weak = NextWeak(1.2f); }
  public bool Biting(float t) => t < biteUntil;
  public void Step(float t, float dt, Skill k, Func<float, float> noise, Func<float> u, Random r, Func<float, float, float, float> dA) {
    rotation += Spin * dt;
    if (!planned) { planned = true; if (u() >= k.lapse) { float need = Rep(-MathF.PI / 2 - AngleOf(weak)); pressAt = t + need / Spin + noise(k.sigma); } else pressAt = t + Rep(-MathF.PI / 2 - AngleOf(weak)) / Spin + 0.3f + 0.01f; }
    bool press = pressAt > 0 && t >= pressAt && !(pressAt > 0 && planned && false);
    bool lapsedPass = false;
    if (press) { pressAt = -1; }
    if (u() < k.spurious * dt) press = true;
    if (press) {
      float off = Math.Abs(dA(AngleOf(weak), -MathF.PI / 2, 0));
      if (off <= Win) { cuts++; biteUntil = t + BiteSec; weak = NextWeak(1.0f + (float)r.NextDouble() * 0.6f); planned = false; }
      else { biteUntil = 0; misses++; planned = false; }
    } else if (pressAt < 0 && !lapsedPass) { }
    // a lapse: the strand goes by unbitten; re-plan for its next lap
    if (pressAt < 0 && planned && !press) planned = false;
  }
}

record Skill(string name, float sigma, float lapse, float spurious, float react, float ctrl, float dragTime, int candidates, float overreact);
