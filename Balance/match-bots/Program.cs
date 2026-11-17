// Reef Run match bots: N fish vs 1 shark on the 300 x 300 m arena, using the game's numbers:
// speeds 6 / 11 m/s for both, shark lunge 26 m/s x 0.5 s (4 s cooldown), attack dash 12 m/s x 0.18 s,
// snap 1.8 m, graze 2.6 m (15 dmg, 12 s wound = no escape), jaws: 4.5 s to mash free
// (meter +0.1 per press, drains at Lerp(9, 3.5, health) x 0.1 /s), break free -30 health.
// Hunger: fish 0.6/s then 0.5 dmg/s; shark 0.6/s then 4 dmg/s, eats flock fish (+20) or players (+60).
// Clock 600 s minus task rewards; then a random wall lifts and fish must reach it.
using System.Numerics;
var cfg = Config.Default;
string mode = args.Length > 0 ? args[0] : "all";
var rng = new Random(777);
float U() => (float)rng.NextDouble();
float Gauss(float sd) { double a = 1 - rng.NextDouble(), b = rng.NextDouble(); return (float)(Math.Sqrt(-2 * Math.Log(a)) * Math.Cos(2 * Math.PI * b)) * sd; }
Vector2 RandPos(float m = 25) => new(-150 + m + U() * (300 - 2 * m), -150 + m + U() * (300 - 2 * m));

Result Play(Config c, int nFish, FishSkill fs, SharkSkill ss) {
  var r = new Result();
  // ---- tasks (what's in IslandTest + stations): reward, fish needed, work seconds (bots / guesses)
  var tasks = new List<Task>();
  void Add(string n, float rew, int need, float work, int count = 1) { for (int i = 0; i < count; i++) tasks.Add(new Task { name = n, reward = rew, need = need, work = work * fs.workScale * (0.8f + 0.4f * U()), pos = RandPos() }); }
  Add("sequence", 40, 2, 45, 4);         // NetworkedSequenceTask x4 (3 rounds, 10 s shows)
  Add("hold", 20, 1, 6);                 // HoldTask (3 s hold)
  Add("move", 25, 1, 35);                // MoveObjectsTask (guess)
  Add("leak", 20, 1, 13, c.leaks);
  Add("coral", 15, 1, 10, c.corals);
  Add("line", 15, 1, 15, c.lines);       // bots 8 s, people slower
  Add("turtle", 45, 2, 45, c.turtles);   // 33 s bites + finding the matching knot (IslandTest has 3)
  // trash: 24 one-point pieces and 10 two-fish net balls; 4 s a point, +30 s once 9 points are in
  void AddTrash(string n, float rew, int need, float work, int count) { for (int i = 0; i < count; i++) tasks.Add(new Task { name = n, reward = rew, need = need, work = work * (0.8f + 0.4f * U()), pos = RandPos() }); }
  float solo = c.oldTrash ? fs.trashSoloOld : fs.trashSolo, duo = c.oldTrash ? fs.trashDuoOld : fs.trashDuo;
  AddTrash("trash", 4, 1, solo, 24); AddTrash("trashball", 8, 2, duo, 10);
  var fish = new List<Fish>();
  for (int i = 0; i < nFish; i++) fish.Add(new Fish { pos = RandPos(40), id = i });
  var sh = new Shark { pos = RandPos(40) };
  float clock = c.roundSeconds, t = 0; const float dt = 0.1f;
  bool escape = false; Vector2 exit = default; int exitSide = 0;
  Vector2 WallPoint(Vector2 p) => exitSide < 2 ? new(p.X, exit.Y * 1.02f) : new(exit.X * 1.02f, p.Y);
  bool Past(Vector2 p) => exitSide switch { 0 => p.Y > 150, 1 => p.Y < -150, 2 => p.X > 150, _ => p.X < -150 }; float exitOpensAt = 0;
  var markers = new List<(Vector2 p, float until)>();

  bool Alive(Fish f) => f.outcome == 0;
  while (t < 1500) {
    t += dt;
    if (!escape) { clock -= dt; if (clock <= 0) { escape = true; exitSide = rng.Next(4); exit = exitSide switch { 0 => new(0, 150), 1 => new(0, -150), 2 => new(150, 0), _ => new(-150, 0) }; exitOpensAt = t + 10; r.clockEndedAt = t; } }
    int alive = fish.Count(Alive); if (alive == 0) break;
    // ---------------- shark
    sh.hunger -= 0.6f * dt;
    if (sh.hunger <= 0) { sh.hunger = 0; sh.health -= 4 * dt; if (sh.health <= 0) { r.sharkStarved = true; foreach (var f in fish) if (Alive(f)) { f.outcome = 3; } break; } }
    // ---------------- fish
    foreach (var f in fish) {
      if (!Alive(f)) continue;
      f.hunger -= 0.6f * dt; if (f.hunger <= 0) { f.hunger = 0; f.health -= 0.5f * dt; if (f.health <= 0) { f.outcome = 2; r.starved++; continue; } }
      if (f.inJaws > 0) continue;
      float d = Vector2.Distance(f.pos, sh.pos);
      // noticing the shark: inside fish sight (31 m fog), slower while heads-down in a minigame
      bool sees = d < c.fishSight * (f.working != null ? 0.75f : 1f);
      if (!f.alert && sees && U() < (f.working != null ? fs.noticeWorking : fs.noticeSwimming) * dt) { f.alert = true; f.alertAt = t; }
      if (f.alert && !sees && d > c.fishSight * 1.4f) f.alert = false;
      if (f.alert && f.buried) { // stay down, maybe twitch
        f.breath -= dt / 37f; if (U() < fs.twitchPerSec * dt) { f.twitchedAt = t; f.breath -= 0.04f; }
        if (f.breath <= 0 || d > 45) { f.buried = false; f.alert = d < c.fishSight; }
        continue;
      }
      if (f.alert) {
        if (f.working != null) { f.working.workers.Remove(f); f.working = null; f.leftWork++; }
        // dig in if the shark is still far and there's sand, otherwise boost away
        if (!f.triedBury && d > 14 && U() < fs.buryChoice) { f.buried = true; f.breath = 1; f.triedBury = true; r.hides++; continue; }
        f.triedBury = true;
        var away = Vector2.Normalize(f.pos - sh.pos + new Vector2(Gauss(3), Gauss(3)));
        if (escape) away = Vector2.Normalize(away + Vector2.Normalize(WallPoint(f.pos) - f.pos) * 1.2f);
        float sp = Math.Min(11, f.speed + 14 * dt); f.speed = sp;
        f.pos += away * sp * dt;
        // the arena wall makes you turn - the shark cuts the corner
        if ((Math.Abs(f.pos.X) > 145 || Math.Abs(f.pos.Y) > 145) && !(escape && t > exitOpensAt)) { f.pos = Vector2.Clamp(f.pos, new(-145), new(145)); f.speed *= 0.6f; }
        if (escape && t > exitOpensAt && Past(f.pos)) { f.outcome = 1; r.escaped++; }
        if (t - f.alertAt > 4 && d > c.fishSight) { f.alert = false; f.triedBury = false; }
        continue;
      }
      f.triedBury = false;
      f.speed = 6;
      // what to do: escape > eat > task
      Vector2 goal; bool atGoal;
      if (escape) { goal = t > exitOpensAt ? WallPoint(f.pos) : WallPoint(f.pos) * 0.85f; atGoal = false; f.speed = 11; if (t > exitOpensAt && Past(f.pos)) { f.outcome = 1; r.escaped++; continue; } }
      else if (f.hunger < 45) { if (f.food == null) f.food = f.pos + Vector2.Normalize(new Vector2(Gauss(1), Gauss(1))) * (10 + U() * 25); goal = f.food.Value; atGoal = Vector2.Distance(f.pos, goal) < 2; if (atGoal) { f.hunger = Math.Min(100, f.hunger + 30); f.food = null; } }
      else {
        if (f.working != null) { // working
          var tk = f.working;
          if (tk.workers.Count >= tk.need) { tk.left -= dt / tk.workers.Count * Math.Min(tk.workers.Count, tk.need) / (tk.need); }
          if (tk.left <= 0 && !tk.done) { tk.done = true; float rw = tk.reward; if (tk.name.StartsWith("trash")) { int before = r.trashPts; r.trashPts += tk.need == 2 ? 2 : 1; if (before < 9 && r.trashPts >= 9) rw += 30; } clock -= rw * c.rewardScale; r.cut += rw * c.rewardScale; r.tasksDone++; markers.Add((tk.pos, t + 12)); foreach (var w in tk.workers.ToList()) w.working = null; tk.workers.Clear(); }
          continue;
        }
        var target = tasks.Where(k => !k.done && (k.workers.Count < k.need)).OrderBy(k => Vector2.Distance(k.pos, f.pos) - (k.workers.Count > 0 ? 60 : 0) + (k.need > 1 && alive < 2 ? 1e6f : 0)).FirstOrDefault();
        if (target == null || target.need > alive) { goal = f.pos + new Vector2(Gauss(5), Gauss(5)); atGoal = false; }
        else { goal = target.pos; atGoal = Vector2.Distance(f.pos, goal) < 3; if (atGoal) { f.working = target; target.workers.Add(f); if (target.left == 0) target.left = target.work; } }
      }
      if (!atGoal) { var dir = goal - f.pos; if (dir.Length() > 0.01f) f.pos += Vector2.Normalize(dir) * Math.Min(f.speed * dt, dir.Length()); }
    }
    // ---------------- shark AI
    foreach (var f in fish) if (f.inJaws > 0) {
      // mashing: the meter drains faster the weaker the fish
      float hold = 9 + (3.5f - 9) * Math.Clamp(f.health / 100, 0, 1);
      if (!f.noEscape) f.meter += (0.1f * Math.Max(0, fs.mash + Gauss(1)) - 0.1f * hold) * dt;
      f.meter = Math.Max(0, f.meter); f.inJaws -= dt;
      if (f.meter >= 1) { f.inJaws = 0; f.health = Math.Max(5, f.health - c.breakFreeDamage); f.bleedUntil = t + 20; f.alert = true; f.alertAt = t; f.speed = 10; r.breakFrees++; f.lastFreed = t; sh.stunUntil = t + c.freeStun; sh.target = f; }
      else if (f.inJaws <= 0) { f.inJaws = 0; f.outcome = 4; r.eaten++; if (escape) r.eatenAfterClock++; sh.hunger = Math.Min(100, sh.hunger + 60); sh.target = null; sh.busyUntil = t + 3; }
    }
    if (t < sh.busyUntil || (c.stunFreezes && t < sh.stunUntil) || fish.Any(f => f.inJaws > 0)) continue;
    bool noBites = t < sh.stunUntil;   // SharkAbilities.Stun: no bites, still swims
    // feed on schools when hungry (each flock fish +20, takes a while to catch)
    if (sh.hunger < ss.feedBelow && sh.target == null) { sh.busyUntil = t + c.flockCatchSeconds * (0.6f + 0.8f * U()); sh.hunger = Math.Min(100, sh.hunger + 20); r.flockEaten++; continue; }
    // sonar: reveals moving fish within 40 m every 25 s
    if (t >= sh.sonarReady) { sh.sonarReady = t + 25; foreach (var f in fish) if (Alive(f) && f.inJaws == 0 && !f.buried && f.working == null && Vector2.Distance(f.pos, sh.pos) < 40) { sh.target ??= f; } }
    // spotting: 55 m shark fog, reefs/kelp block some sightlines; bleeding fish leave a trail
    if (sh.target == null || !Alive(sh.target)) {
      sh.target = null;
      Fish best = null; float bd = 1e9f;
      foreach (var f in fish) {
        if (!Alive(f) || f.inJaws > 0) continue;
        float d = Vector2.Distance(f.pos, sh.pos);
        bool heard = f.buried && t - f.twitchedAt < 2 && d < 40;
        // picking a player out from the schools: easy at a task, harder while it swims with the flock
        float rate = (f.working != null ? c.spotWorking : c.spotSwimming) * ss.spot * c.lineOfSight;
        bool visible = !f.buried && d < c.sharkSight && U() < rate * dt;
        if (escape && !f.buried) visible = true;   // the exit reveals every fish at any distance
        if (t < f.bleedUntil && d < c.bleedTrack) visible = true;
        if ((visible || heard) && d < bd) { bd = d; best = f; }
      }
      sh.target = best;
    }
    Vector2 sgoal;
    if (sh.target != null) {
      var f = sh.target; float d = Vector2.Distance(f.pos, sh.pos);
      if (f.buried && t - f.twitchedAt > 2 && d > 6) { // lost it in the sand
        if (U() < 0.5f * dt) sh.target = null;
      }
      // lunge when close: 13 m in 0.5 s; a fleeing fish keeps 5.5 m of that
      float reach = 13 - (f.alert ? f.speed * 0.5f : 0) + 1.8f;
      if (!noBites && t >= sh.lungeReady && d < reach && !(f.buried && t - f.twitchedAt > 2 && U() < 0.7f)) {
        sh.lungeReady = t + c.lungeCooldown; sh.lunges++;
        bool dodge = f.alert && !f.buried && U() < fs.dodge * ss.lungeAim;
        if (!dodge) Catch(f);
        else if (U() < 0.35f) { f.health = Math.Max(5, f.health - 15); f.woundUntil = t + 12; f.bleedUntil = t + 20; r.grazes++; }
        sh.pos += Vector2.Normalize(f.pos - sh.pos + new Vector2(0.01f, 0)) * Math.Min(d, 12);
        continue;
      }
      if (!noBites && d < 4 && t >= sh.biteReady) { sh.biteReady = t + 0.6f; if (!f.alert || U() > fs.dodge * 0.8f) { Catch(f); continue; } }
      // the chase: equal top speed, so only mistakes and walls close the gap
      sgoal = f.pos;
      float gain = f.alert ? fs.chaseLoss * ss.chase : 0;
      sh.pos += Vector2.Normalize(sgoal - sh.pos + new Vector2(0.001f, 0)) * (f.alert ? Math.Min(d, f.speed * dt + gain * dt) : 11 * dt);
      sh.chaseTime += dt;
      if ((d > c.sharkSight * 1.6f && !escape) || (sh.chaseTime > ss.giveUp && d > 8)) { sh.target = null; sh.chaseTime = 0; }
    } else {
      sh.chaseTime = 0;
      // where to look: the exit once it's open, fresh task markers, tasks fish will come to, else roam
      markers.RemoveAll(m => m.until < t);
      if (escape) { var live = fish.Where(Alive).ToList(); var c0 = live.Count > 0 ? live.Aggregate(Vector2.Zero, (a, f) => a + f.pos) / live.Count : Vector2.Zero; sgoal = WallPoint(c0) * 0.93f; }
      else if (markers.Count > 0 && ss.useMarkers) sgoal = markers[^1].p;
      else { if (sh.wander == null || Vector2.Distance(sh.wander.Value, sh.pos) < 5) { var open = tasks.Where(k => !k.done).ToList(); sh.wander = open.Count > 0 && U() < ss.patrolTasks ? open[rng.Next(open.Count)].pos : RandPos(); } sgoal = sh.wander.Value; }
      var dv = sgoal - sh.pos; if (dv.Length() > 1) sh.pos += Vector2.Normalize(dv) * 6 * dt;
    }
    void Catch(Fish f) {
      if (f.buried) f.buried = false;
      if (f.working != null) { f.working.workers.Remove(f); f.working = null; }
      f.inJaws = t < f.woundUntil ? 1.5f : 4.5f; if (f.lastFreed > 0 && t - f.lastFreed < 25) r.recatches++; if (r.firstCatch == 0) r.firstCatch = t; if (!f.alert) r.unaware++; f.noEscape = t < f.woundUntil; f.meter = 0; r.catches++;
    }
  }
  r.nFish = nFish;
  int made = fish.Count(f => f.outcome == 1 || f.outcome == 3);
  r.fishWin = made * 2 > nFish; r.made = made; r.length = t;
  return r;
}

void Run(string label, Config c, int n, FishSkill fs, SharkSkill ss, int runs = 600) {
  var res = new List<Result>(); for (int i = 0; i < runs; i++) res.Add(Play(c, n, fs, ss));
  float wr = res.Count(x => x.fishWin) / (float)runs;
  Console.WriteLine($"{label,-44} fish win {wr,5:P0}  made it {res.Average(x => x.made) / n,4:P0}  eaten {res.Average(x => x.eaten),3:0.0}  starved(fish) {res.Average(x => x.starved),3:0.0}  shark starved {res.Count(x => x.sharkStarved) / (float)runs,4:P0}  clock cut {res.Average(x => x.cut),4:0}s  eaten after clock {res.Average(x => x.eatenAfterClock),3:0.0}  clock out at {res.Where(x => x.clockEndedAt > 0).Select(x => x.clockEndedAt).DefaultIfEmpty(0).Average(),4:0}s  catches {res.Average(x => x.catches),3:0.0} (freed {res.Average(x => x.breakFrees),3:0.0}, unaware {res.Sum(x => x.unaware) / (float)Math.Max(1, res.Sum(x => x.catches)),3:P0}, re-caught <25s {res.Sum(x => x.recatches) / (float)Math.Max(1, res.Sum(x => x.catches)),3:P0})  first catch {res.Average(x => x.firstCatch),3:0}s");
}

var fsk = new Dictionary<string, FishSkill> { ["sharp"] = FishSkill.Sharp, ["average"] = FishSkill.Average, ["sloppy"] = FishSkill.Sloppy };
var ssk = new Dictionary<string, SharkSkill> { ["sharp"] = SharkSkill.Sharp, ["average"] = SharkSkill.Average, ["sloppy"] = SharkSkill.Sloppy };
if (mode == "levers") { Levers(); return; }
if (mode == "now") { Now(); return; }
Console.WriteLine("== skill vs skill, 4 fish");
foreach (var f in fsk) foreach (var s in ssk) Run($"{f.Key} fish vs {s.Key} shark", cfg, 4, f.Value, s.Value);
Console.WriteLine("\n== player count (average vs average)");
for (int n = 2; n <= 6; n++) Run($"{n} fish", cfg, n, FishSkill.Average, SharkSkill.Average);
Console.WriteLine("\n== how sure: average vs average, 4 fish, one guess changed at a time");
Run("as is", cfg, 4, FishSkill.Average, SharkSkill.Average);
Run("flock fish take 2x as long to catch", cfg with { flockCatchSeconds = cfg.flockCatchSeconds * 2 }, 4, FishSkill.Average, SharkSkill.Average);
Run("flock fish take half as long", cfg with { flockCatchSeconds = cfg.flockCatchSeconds / 2 }, 4, FishSkill.Average, SharkSkill.Average);
Run("more sightlines blocked (reef, kelp)", cfg with { lineOfSight = cfg.lineOfSight * 0.6f }, 4, FishSkill.Average, SharkSkill.Average);
Run("fish notice the shark 2x slower", cfg, 4, FishSkill.Average with { noticeWorking = FishSkill.Average.noticeWorking / 2, noticeSwimming = FishSkill.Average.noticeSwimming / 2 }, SharkSkill.Average);
Run("fish make chase mistakes 2x as often", cfg, 4, FishSkill.Average with { chaseLoss = FishSkill.Average.chaseLoss * 2 }, SharkSkill.Average);
Run("fish never hide in the sand", cfg, 4, FishSkill.Average with { buryChoice = 0 }, SharkSkill.Average);
Run("trash fix (25 s per piece)", cfg with { trashSeconds = 25 }, 4, FishSkill.Average, SharkSkill.Average);
Run("fish blend into schools 2x better", cfg with { spotSwimming = cfg.spotSwimming / 2 }, 4, FishSkill.Average, SharkSkill.Average);
Run("fish at tasks are 2x easier to spot", cfg with { spotWorking = cfg.spotWorking * 2 }, 4, FishSkill.Average, SharkSkill.Average);
Run("shark ignores task markers", cfg, 4, FishSkill.Average, SharkSkill.Average with { useMarkers = false });

void Levers() {
  { var a0 = FishSkill.Average; var b0 = SharkSkill.Average;
    Console.WriteLine("== the stun as the game does it (no bites, still swims) vs a daze (can't move)");
    foreach (var st in new[] { 1.5f, 4f, 6f, 8f }) { Run($"no bites {st} s", cfg with { freeStun = st }, 4, a0, b0); Run($"dazed {st} s", cfg with { freeStun = st, stunFreezes = true }, 4, a0, b0); }
    Run("no bites 6 s + 450 s + x1.5", cfg with { freeStun = 6, roundSeconds = 450, rewardScale = 1.5f }, 4, a0, b0);
    Run("dazed 3 s + 450 s + x1.5", cfg with { freeStun = 3, stunFreezes = true, roundSeconds = 450, rewardScale = 1.5f }, 4, a0, b0);
    Run("dazed 4 s + 450 s + x1.5", cfg with { freeStun = 4, stunFreezes = true, roundSeconds = 450, rewardScale = 1.5f }, 4, a0, b0);
    Run("dazed 6 s + 450 s + x1.5", cfg with { freeStun = 6, stunFreezes = true, roundSeconds = 450, rewardScale = 1.5f }, 4, a0, b0);
    if (args.Length > 1) return; }
  var a = FishSkill.Average; var b = SharkSkill.Average;
  Console.WriteLine("== uncertain guess: how far a bleeding fish can be tracked");
  Run("bleeding fish tracked within 70 m (as run)", cfg, 4, a, b);
  Run("bleeding fish tracked within 30 m", cfg with { bleedTrack = 30 }, 4, a, b);
  Console.WriteLine(); Console.WriteLine("== rule changes (average vs average, 4 fish)");
  Run("round 450 s instead of 600", cfg with { roundSeconds = 450 }, 4, a, b);
  Run("round 360 s", cfg with { roundSeconds = 360 }, 4, a, b);
  Run("task rewards x1.5", cfg with { rewardScale = 1.5f }, 4, a, b);
  Run("task rewards x2", cfg with { rewardScale = 2f }, 4, a, b);
  Run("break free costs 15 health, not 30", cfg with { breakFreeDamage = 15 }, 4, a, b);
  Run("lunge cooldown 7 s, not 4", cfg with { lungeCooldown = 7 }, 4, a, b);
  Run("round 450 s + break free costs 15", cfg with { roundSeconds = 450, breakFreeDamage = 15 }, 4, a, b);
  Run("round 450 s + rewards x1.5", cfg with { roundSeconds = 450, rewardScale = 1.5f }, 4, a, b);
  Run("shark stunned 4 s after a break-free, not 1.5", cfg with { freeStun = 4 }, 4, a, b);
  Run("shark stunned 6 s", cfg with { freeStun = 6 }, 4, a, b);
  Run("stun 4 s + round 450 s", cfg with { freeStun = 4, roundSeconds = 450 }, 4, a, b);
  Run("stun 6 s + round 450 s", cfg with { freeStun = 6, roundSeconds = 450 }, 4, a, b);
  Run("stun 6 s + round 450 s + rewards x1.5", cfg with { freeStun = 6, roundSeconds = 450, rewardScale = 1.5f }, 4, a, b);
  Console.WriteLine(); Console.WriteLine("== best candidate (stun 6 s + round 450 s + rewards x1.5) across skills and player counts");
  var best = cfg with { freeStun = 6, roundSeconds = 450, rewardScale = 1.5f };
  foreach (var (fn, f) in new[] { ("sharp", FishSkill.Sharp), ("average", a), ("sloppy", FishSkill.Sloppy) }) foreach (var (sn, sk) in new[] { ("sharp", SharkSkill.Sharp), ("average", b), ("sloppy", SharkSkill.Sloppy) }) Run($"{fn} fish vs {sn} shark", best, 4, f, sk, 400);
  for (int n = 2; n <= 6; n++) Run($"{n} fish (average)", best, n, a, b, 400);
  Run("round 450 s + rewards x1.5 + break free 15", cfg with { roundSeconds = 450, rewardScale = 1.5f, breakFreeDamage = 15 }, 4, a, b);
}

void Now() {
  var a = FishSkill.Average; var b = SharkSkill.Average;
  var old = cfg;                                                           // rules this morning (3 turtles, 1/2/1 stations, old trash)
  var applied = cfg with { freeStun = 3, stunFreezes = true, roundSeconds = 450, rewardScale = 1.5f };
  var now = applied with { oldTrash = false, leaks = 2, corals = 3, lines = 2 };
  Console.WriteLine("== average vs average, 4 fish (with the real task list: 3 turtles, 34 trash pieces)");
  Run("rules this morning", old, 4, a, b);
  Run("+ daze 3 s, 7:30 round, rewards x1.5", applied, 4, a, b);
  Run("+ trash fix", applied with { oldTrash = false }, 4, a, b);
  Run("+ trash fix + 2 leaks, 3 coral, 2 lines (now)", now, 4, a, b);
  Console.WriteLine(); Console.WriteLine("== now, skill vs skill (4 fish)");
  foreach (var (fn, f) in new[] { ("sharp", FishSkill.Sharp), ("average", a), ("sloppy", FishSkill.Sloppy) }) foreach (var (sn, sk) in new[] { ("sharp", SharkSkill.Sharp), ("average", b), ("sloppy", SharkSkill.Sloppy) }) Run($"{fn} fish vs {sn} shark", now, 4, f, sk, 400);
  Console.WriteLine(); Console.WriteLine("== now, player count (average vs average)");
  for (int n = 2; n <= 6; n++) Run($"{n} fish", now, n, a, b, 400);
}

record Config(float roundSeconds, float fishSight, float sharkSight, float lineOfSight, float flockCatchSeconds, float trashSeconds, float spotWorking, float spotSwimming, float bleedTrack = 70, float breakFreeDamage = 30, float lungeCooldown = 4, float rewardScale = 1, float freeStun = 1.5f, bool stunFreezes = false, int leaks = 1, int corals = 2, int lines = 1, int turtles = 3, bool oldTrash = true) {
  public static Config Default => new(600, 31, 30, 0.7f, 12, 70, 1.5f, 0.5f);
}
// trash: bags and scraps are pushed by swimming into them (~2 m/s over ~30 m, no minigame), so ~18 s at any skill;
// net balls use the push minigame. Times from Balance/bots (median s per piece: solo, two-fish net ball), before and after the 2026-11-17 fix
record FishSkill(float noticeWorking, float noticeSwimming, float dodge, float mash, float chaseLoss, float twitchPerSec, float buryChoice, float workScale, float trashSolo, float trashDuo, float trashSoloOld, float trashDuoOld) {
  public static FishSkill Sharp => new(0.8f, 1.5f, 0.75f, 9.5f, 0.25f, 0.012f, 0.35f, 0.85f, 18, 25, 18, 31);
  public static FishSkill Average => new(0.45f, 1.0f, 0.55f, 8f, 0.5f, 0.04f, 0.3f, 1f, 18, 37, 18, 97);
  public static FishSkill Sloppy => new(0.25f, 0.6f, 0.35f, 6.5f, 0.9f, 0.13f, 0.2f, 1.3f, 20, 195, 20, 257);
}
record SharkSkill(float spot, float lungeAim, float chase, float giveUp, float feedBelow, float patrolTasks, bool useMarkers) {
  public static SharkSkill Sharp => new(1.3f, 1.25f, 1.3f, 25, 25, 0.8f, true);
  public static SharkSkill Average => new(1f, 1f, 1f, 18, 35, 0.6f, true);
  public static SharkSkill Sloppy => new(0.7f, 0.8f, 0.7f, 12, 45, 0.3f, true);
}
class Task { public string name; public float reward, work, left; public int need; public Vector2 pos; public bool done; public List<Fish> workers = new(); }
class Fish { public int id, outcome, leftWork; public Vector2 pos; public float hunger = 100, health = 100, speed, alertAt, woundUntil, bleedUntil, inJaws, meter, breath, twitchedAt = -99; public float lastFreed; public bool alert, buried, triedBury, noEscape; public Task working; public Vector2? food; }
class Shark { public Vector2 pos; public float hunger = 100, health = 100, lungeReady, biteReady, sonarReady = 10, busyUntil, stunUntil, chaseTime; public int lunges; public Fish target; public Vector2? wander; }
class Result { public bool fishWin, sharkStarved; public int trashPts; public int recatches, unaware; public float firstCatch; public int eatenAfterClock, nFish, made, eaten, starved, catches, breakFrees, grazes, hides, flockEaten, tasksDone, escaped; public float cut, clockEndedAt, length; }
