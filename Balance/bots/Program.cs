// Balance bots for Reef Run's minigames. Each game below mirrors the game's own code and constants
// (TrashMinigameUI, PushableTrash, TrashMinigame, TangledTurtle net game, LeakStation, CoralScrubStation,
// UntangleStation, SandHide). Bots differ only in human-like timing error, lapses and reaction time.
using System.Text;
const float DT = 1f / 60f;
var rng = new Random(12345);
float U() => (float)rng.NextDouble();
float N(float sd) { double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble(); return (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2)) * sd; }
float Clamp01(float x) => Math.Clamp(x, 0f, 1f);
float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
float MoveTowards(float a, float b, float d) => Math.Abs(b - a) <= d ? b : a + Math.Sign(b - a) * d;
float DeltaAngleRad(float a, float b) { float d = (b - a) % (2 * MathF.PI); if (d > MathF.PI) d -= 2 * MathF.PI; if (d < -MathF.PI) d += 2 * MathF.PI; return d; }

var skills = new[] {
  new Skill("sharp",   0.025f, 0.02f, 0.01f, 0.22f, 0.08f, 1.2f, 60, 0.0f),
  new Skill("average", 0.050f, 0.06f, 0.04f, 0.30f, 0.15f, 2.0f, 25, 0.15f),
  new Skill("sloppy",  0.090f, 0.15f, 0.10f, 0.45f, 0.25f, 3.0f, 10, 0.35f),
};
const int RUNS = 400;
var sb = new StringBuilder();
void Out(string s) { Console.WriteLine(s); sb.AppendLine(s); }
string Stats(List<float> v) { if (v.Count == 0) return "n/a"; v.Sort(); float P(float q) => v[(int)Math.Clamp(q * (v.Count - 1), 0, v.Count - 1)]; return $"median {P(.5f):0.0}  (p10 {P(.1f):0.0} / p90 {P(.9f):0.0})"; }

// ---------------------------------------------------------------- trash needle (TrashMinigameUI)
// ball speed under PushableTrash physics (minigame drive + water drag + ground friction)
float BallStep(ref float v, float S, float pushSpeed, float dt) {
  float target = Math.Min(3f, pushSpeed * S);
  if (S > 0.01f && v < target) v += Math.Min(3f * dt, target - v);
  v *= MathF.Exp(-0.8f * dt); v *= MathF.Exp(-1f * dt);
  return v * dt;
}

(float time, bool ok, float avgS, float hitRate) TrashSolo(Skill k, float pushSpeed, float dist) {
  var n = new Needle(rng); float t = 0, v = 0, x = 0, sSum = 0; int frames = 0;
  while (t < 240) { n.Step(t, DT, k, N, U); x += BallStep(ref v, n.speed, pushSpeed, DT); sSum += n.speed; frames++; t += DT; if (x >= dist) return (t, true, sSum / frames, n.hits / (float)Math.Max(1, n.hits + n.misses)); }
  return (t, false, sSum / frames, 0);
}
(float time, bool ok, float avgS, float syncRate) TrashTogether(Skill k, float dist) {
  var a = new Needle(rng); var b = new Needle(rng); float t = 0, v = 0, x = 0, shared = 0, sSum = 0; int frames = 0, syncs = 0, hitsTot = 0;
  float? ha = null, hb = null;
  while (t < 300) {
    float ra = a.Step(t, DT, k, N, U), rb = b.Step(t, DT, k, N, U);
    void Hit(ref float? mine, float acc, float? other) {
      mine = t; hitsTot++;
      if (other.HasValue && t - other.Value <= 0.3f) { shared = Math.Min(1, shared + 0.25f * Lerp(0.4f, 1f, acc)); syncs++; ha = null; hb = null; }
    }
    if (ra >= 0) Hit(ref ha, ra, hb);
    if (rb >= 0) Hit(ref hb, rb, ha);
    shared = Math.Max(0, shared - 0.06f * DT);
    x += BallStep(ref v, shared, 1.3f, DT); sSum += shared; frames++; t += DT;
    if (x >= dist) return (t, true, sSum / frames, syncs * 2f / Math.Max(1, hitsTot));
  }
  return (t, false, sSum / frames, syncs * 2f / Math.Max(1, hitsTot));
}

// ---------------------------------------------------------------- turtle net (TangledTurtle)
(float time, bool ok, float bothFrac) Turtle(Skill k) {
  // two fish on a matching pair, then swim to the other pair (~4 s, plus finding it: the words)
  float t = 0, both = 0, total = 0; int pair = 0; float p = 0, lonely = 0;
  var a = new Biter(rng); var b = new Biter(rng);
  while (t < 300) {
    a.Step(t, DT, k, N, U, rng, (x, y, z) => DeltaAngleRad(x, y)); b.Step(t, DT, k, N, U, rng, (x, y, z) => DeltaAngleRad(x, y));
    bool ab = a.Biting(t), bb = b.Biting(t);
    if (ab && bb) { p = Math.Min(1, p + DT / 6f); lonely = 0; both += DT; }
    else if (ab || bb) { lonely += DT; if (lonely > 0.6f) p = Math.Max(0, p - 0.25f * DT); }
    else lonely = 0;
    total += DT; t += DT;
    if (p >= 1) { pair++; p = 0; if (pair == 2) return (t + 4f, true, both / total); t += 4f; a = new Biter(rng); b = new Biter(rng); }
  }
  return (t, false, both / Math.Max(total, 1e-3f));
}

// ---------------------------------------------------------------- leak (LeakStation)
float Noise1(float seed, float x) { int i = (int)MathF.Floor(x); float f = x - i; float h(int n) { uint s = (uint)(n * 374761393 + (int)(seed * 668265263)); s = (s ^ (s >> 13)) * 1274126177; return ((s ^ (s >> 16)) & 0xffff) / 65535f; } float w = f * f * (3 - 2 * f); return 0.5f + (Lerp(h(i), h(i + 1), w) - 0.5f) * 0.8f; }
(float time, bool ok, int bursts) Leak(Skill k) {
  float value = 0.5f, vel = 0, seal = 0, outFor = 0, seed = U() * 50, t = 0, nextTap = 0; int bursts = 0;
  var hist = new Queue<(float t, float v, float vel)>();
  while (t < 180) {
    float band = Lerp(0.32f, 0.1f, seal);
    float push = (Noise1(seed, t * 0.7f) - 0.5f) * 2f * Lerp(0.55f, 1f, seal);
    hist.Enqueue((t, value, vel)); while (hist.Peek().t < t - k.react) hist.Dequeue();
    var seen = hist.Peek();
    // bot: lead the needle by its drift, tap toward the middle when it's heading out of the band
    float predict = seen.v - 0.5f + seen.vel * 2.2f * (k.react + 0.15f) * (1 - k.overreact);
    float thr = band * (0.25f + 0.25f * k.overreact);
    if (t >= nextTap && Math.Abs(predict) > thr && U() >= k.lapse * 0.5f) {
      int taps = U() < k.overreact * 0.3f ? 2 : 1;
      vel += (predict > 0 ? -0.16f : 0.16f) * taps; nextTap = t + 0.12f + k.react * 0.6f;
    }
    if (U() < k.spurious * DT) vel += U() < 0.5f ? 0.16f : -0.16f;
    vel += push * DT * 0.9f; vel *= MathF.Exp(-1.6f * DT); value += vel * DT * 2.2f;
    if (value < 0 || value > 1) { value = Clamp01(value); vel = 0; }
    bool inBand = Math.Abs(value - 0.5f) <= band / 2;
    if (inBand) { seal = Math.Min(1, seal + DT / 9f); outFor = 0; } else outFor += DT;
    if (outFor >= 1.3f) { outFor = 0; seal = Math.Max(0, seal - 0.25f); value = 0.5f; vel = 0; bursts++; }
    t += DT;
    if (seal >= 1) return (t, true, bursts);
  }
  return (t, false, bursts);
}

// ---------------------------------------------------------------- coral (CoralScrubStation)
(float time, bool ok, int sands) Coral(Skill k) {
  float clean = 0, sand = 0, sp = 0, stun = 0, t = 0, want = Lerp(0.42f, 0.6f, k.overreact * 2.5f), cur = want; int sands = 0;
  while (t < 120) {
    cur += (want - cur) * 1.5f * DT + N(k.ctrl) * MathF.Sqrt(DT) * 1.6f; cur = Math.Clamp(cur, 0, 1.3f);
    sp = Lerp(sp, Clamp01(cur), 1 - MathF.Exp(-8 * DT));
    bool stunned = t < stun;
    if (!stunned && sp >= 0.22f && sp <= 0.7f && sand < 0.6f) clean = Math.Min(1, clean + DT / 9f);
    if (sp > 0.85f) sand += DT * 1.1f; else sand = Math.Max(0, sand - DT * 0.35f);
    if (sand >= 1 && !stunned) { stun = t + 1.5f; clean = Math.Max(0, clean - 0.08f); sands++; }
    sand = Math.Min(sand, 1); t += DT;
    if (clean >= 1) return (t, true, sands);
  }
  return (t, false, sands);
}

// ---------------------------------------------------------------- untangle (UntangleStation)
int[,] E = { {0,1},{1,2},{2,3},{3,4},{4,5},{5,6},{6,0},{0,2},{0,4},{4,6} };
bool Seg(System.Numerics.Vector2 a, System.Numerics.Vector2 b, System.Numerics.Vector2 c, System.Numerics.Vector2 d) {
  float Cr(System.Numerics.Vector2 o, System.Numerics.Vector2 p, System.Numerics.Vector2 q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
  float d1 = Cr(c, d, a), d2 = Cr(c, d, b), d3 = Cr(a, b, c), d4 = Cr(a, b, d); return d1 * d2 < 0 && d3 * d4 < 0; }
int Crossings(System.Numerics.Vector2[] n) { int c = 0; for (int i = 0; i < 10; i++) for (int j = i + 1; j < 10; j++) { int a = E[i,0], b = E[i,1], x = E[j,0], y = E[j,1]; if (a == x || a == y || b == x || b == y) continue; if (Seg(n[a], n[b], n[x], n[y])) c++; } return c; }
(float time, bool ok, int moves) Untangle(Skill k) {
  var n = new System.Numerics.Vector2[7];
  for (int tries = 0; tries < 200; tries++) {
    for (int i = 0; i < 7; i++) { var p = default(System.Numerics.Vector2); for (int q = 0; q < 30; q++) { p = new(0.05f + U() * .9f, 0.05f + U() * .9f); bool ok = true; for (int j = 0; j < i; j++) if ((n[j] - p).LengthSquared() < .22f * .22f) ok = false; if (ok) break; } n[i] = p; }
    if (Crossings(n) >= 5) break;
  }
  float t = 1.5f; int moves = 0; // read the board first
  while (Crossings(n) > 0 && t < 180) {
    int bestN = 0; var bestP = n[0]; int bestC = int.MaxValue;
    for (int c = 0; c < k.candidates; c++) {
      int i = rng.Next(7); var p = new System.Numerics.Vector2(U(), U()); var old = n[i]; n[i] = p; int cc = Crossings(n); n[i] = old;
      if (cc < bestC || (cc == bestC && U() < .3f)) { bestC = cc; bestN = i; bestP = p; }
    }
    n[bestN] = bestP; moves++; t += k.dragTime + Math.Abs(N(k.dragTime * 0.3f));
  }
  bool solved = Crossings(n) == 0;
  return (t + 0.6f, solved, moves);
}

// ---------------------------------------------------------------- hiding (SandHide)
(float time, int twitches, int missed) Hide(Skill k) {
  const float BI = 1.1f, BW = 0.17f; float breath = 1, clock = 0, t = 0; int last = -1; bool tapped = false; int tw = 0, miss = 0;
  float nextPress = BI + N(k.sigma);
  while (t < 120) {
    clock += DT; t += DT;
    float phase = clock / BI; int beat = (int)MathF.Round(phase); float offBy = Math.Abs(phase - beat) * BI;
    int passed = (int)MathF.Floor(phase - BW / BI);
    if (passed > last && passed >= 1) { if (!tapped) { breath -= 0.05f; miss++; } last = passed; tapped = false; }
    bool press = false;
    if (clock >= nextPress) { press = U() >= k.lapse; int nb = (int)MathF.Round(nextPress / BI) + 1; nextPress = nb * BI + N(k.sigma); }
    if (U() < k.spurious * DT) press = true;
    if (press) {
      if (offBy <= BW && beat > last && !tapped) { tapped = true; breath = Math.Min(1, breath + 0.01f); }
      else { breath -= 0.04f; tw++; }
    }
    breath -= DT / 28f;
    if (breath <= 0) return (t, tw, miss);
  }
  return (t, tw, miss);
}

// ================================================================ run
Out("Reef Run minigame balance bots - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
Out($"{RUNS} runs per game per skill. Times in seconds of play (no swimming).\n");
foreach (var k in skills) {
  Out($"=== {k.name} (timing sd {k.sigma * 1000:0} ms, lapses {k.lapse:P0}, stray presses {k.spurious:0.00}/s, reaction {k.react:0.00} s)");
  // trash solo pieces
  foreach (var (name, ps) in new[] { ("bag", 2f), ("scrap", 1.8f) }) {
    var times = new List<float>(); float sAcc = 0, hr = 0; int fails = 0;
    for (int i = 0; i < RUNS; i++) { float d = 14 + U() * 31 - 2.5f; var r = TrashSolo(k, ps, d); if (r.ok) times.Add(r.time); else fails++; sAcc += r.avgS; hr += r.hitRate; }
    Out($"  trash {name,-6} (1 pt, 1 fish): {Stats(times)}  avg speed {sAcc / RUNS:P0}  hit rate {hr / RUNS:P0}  stalled {fails}");
  }
  { var times = new List<float>(); float sAcc = 0, syn = 0; int fails = 0;
    for (int i = 0; i < RUNS; i++) { float d = 14 + U() * 31 - 2.5f; var r = TrashTogether(k, d); if (r.ok) times.Add(r.time); else fails++; sAcc += r.avgS; syn += r.syncRate; }
    Out($"  trash netball (2 pt, 2 fish): {Stats(times)}  avg speed {sAcc / RUNS:P0}  hits that synced {syn / RUNS:P0}  stalled(>300s) {fails}"); }
  { var times = new List<float>(); float bf = 0; int fails = 0;
    for (int i = 0; i < RUNS; i++) { var r = Turtle(k); if (r.ok) times.Add(r.time); else fails++; bf += r.bothFrac; }
    Out($"  turtle net   (2 fish, 45 s):  {Stats(times)}  both biting {bf / RUNS:P0} of the time  failed {fails}"); }
  { var times = new List<float>(); int fails = 0, b = 0;
    for (int i = 0; i < RUNS; i++) { var r = Leak(k); if (r.ok) times.Add(r.time); else fails++; b += r.bursts; }
    Out($"  leak         (20 s):          {Stats(times)}  bursts/run {b / (float)RUNS:0.0}  not sealed in 180 s {fails}"); }
  { var times = new List<float>(); int fails = 0, s = 0;
    for (int i = 0; i < RUNS; i++) { var r = Coral(k); if (r.ok) times.Add(r.time); else fails++; s += r.sands; }
    Out($"  coral        (15 s):          {Stats(times)}  sand stuns/run {s / (float)RUNS:0.0}  failed {fails}"); }
  { var times = new List<float>(); int fails = 0, m = 0;
    for (int i = 0; i < RUNS / 4; i++) { var r = Untangle(k); if (r.ok) times.Add(r.time); else fails++; m += r.moves; }
    Out($"  untangle     (15 s):          {Stats(times)}  drags/run {m / (RUNS / 4f):0.0}  gave up {fails}"); }
  { var times = new List<float>(); float tw = 0, ms = 0;
    for (int i = 0; i < RUNS; i++) { var r = Hide(k); times.Add(r.time); tw += r.twitches; ms += r.missed; }
    Out($"  hiding (time until breath runs out): {Stats(times)}  twitches/run {tw / RUNS:0.0}  missed beats/run {ms / RUNS:0.0}"); }
  Out("");
}
Out("=== trash variants (bag, 1 fish): sweep / box width / miss penalty");
foreach (var (sw, bx, mp) in new[] { (0.85f,0.2f,0.15f), (0.85f,0.3f,0.15f), (0.85f,0.2f,0.08f), (0.85f,0.3f,0.08f), (1.0f,0.3f,0.08f) }) {
  Needle.sweep = sw; Needle.box = bx; Needle.missPen = mp;
  var line = $"  {sw:0.00}s / {bx:0.00} / {mp:0.00}:";
  foreach (var k in skills) { var times = new List<float>(); float hr = 0; for (int i = 0; i < RUNS; i++) { var r = TrashSolo(k, 2f, 14 + U() * 31 - 2.5f); times.Add(r.time); hr += r.hitRate; } times.Sort(); line += $"  {k.name} {times[times.Count/2]:0} s ({hr/RUNS:P0} hits)"; }
  Out(line);
}
File.WriteAllText("results.txt", sb.ToString());

