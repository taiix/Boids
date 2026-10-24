// The REEF RUN title's waterline, animated. The static logo bakes the effect in (reefrun_gametitle.py,
// _surface): crisp letters above a thin bright line at 52 % of the letter height, and below it a sine
// refraction ripple with a light sea tint. Here the same layers are drawn every frame instead: the dry
// letters (depth + bite, no water) above a travelling wavy surface, row-shifted by a moving ripple below
// it, plus the bright line masked to the letters. Geometry is in the exported image's pixels (1799 x 603).

const GEO = { width: 1799, height: 603, lineY: 271.92, letterH: 357.87 };

// Proportions of the letter height. The baked logo uses ripple 0.018 / period 0.30 and line 0.010;
// the live wave is a little stronger so it reads as moving water.
const RIPPLE_AMP = 0.022;
const RIPPLE_PERIOD = 0.3;
const RIPPLE_SPEED = 0.9;    // ripple cycles per second, flowing down the letters
const CALM_DEPTH = 0.1;      // the ripple fades in over this depth below the surface
const SURFACE_AMP = 0.016;
const SURFACE_WAVELENGTH = 0.9;
const SURFACE_SPEED = 0.35;  // wavelengths per second, travelling left to right
const LINE_WIDTH = 0.022;
const TINT = "rgba(90, 215, 220, 0.22)";

function load(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = reject;
    img.src = src;
  });
}

function layer(w: number, h: number) {
  const c = document.createElement("canvas");
  c.width = w;
  c.height = h;
  return c.getContext("2d")!;
}

export async function startWaterLogo(canvas: HTMLCanvasElement, still: HTMLElement, animate: boolean) {
  const [letters, shadow] = await Promise.all([load("/media/logo_letters.png"), load("/media/logo_shadow.png")]);
  const ctx = canvas.getContext("2d");
  if (!ctx) return;

  let k = 1;
  let dry: CanvasRenderingContext2D, under: CanvasRenderingContext2D, line: CanvasRenderingContext2D;

  const resize = () => {
    const css = canvas.getBoundingClientRect().width;
    const w = Math.max(1, Math.min(GEO.width, Math.round(css * Math.min(window.devicePixelRatio || 1, 2.5))));
    k = w / GEO.width;
    const h = Math.round(GEO.height * k);
    if (canvas.width === w && canvas.height === h) return;
    canvas.width = w;
    canvas.height = h;
    dry = layer(w, h);
    dry.drawImage(letters, 0, 0, w, h);
    under = layer(w, h);
    line = layer(w, h);
  };

  // The surface height at x (canvas px), at time t (s): two travelling sines so it never looks mechanical.
  const surface = (x: number, t: number) => {
    const lh = GEO.letterH * k;
    const lam = lh * SURFACE_WAVELENGTH;
    return (
      GEO.lineY * k +
      lh * SURFACE_AMP * Math.sin((2 * Math.PI * x) / lam - 2 * Math.PI * SURFACE_SPEED * t) +
      lh * SURFACE_AMP * 0.45 * Math.sin((2 * Math.PI * x) / (lam * 0.53) + 2 * Math.PI * SURFACE_SPEED * 0.7 * t)
    );
  };

  const surfacePath = (c: CanvasRenderingContext2D, t: number, close: "above" | "below" | null) => {
    const w = canvas.width, h = canvas.height, step = Math.max(2, Math.round(4 * k));
    c.beginPath();
    c.moveTo(0, surface(0, t));
    for (let x = step; x <= w + step; x += step) c.lineTo(x, surface(x, t));
    if (close === "above") { c.lineTo(w, 0); c.lineTo(0, 0); c.closePath(); }
    if (close === "below") { c.lineTo(w, h); c.lineTo(0, h); c.closePath(); }
  };

  const draw = (t: number) => {
    const w = canvas.width, h = canvas.height, lh = GEO.letterH * k;
    ctx.clearRect(0, 0, w, h);
    ctx.drawImage(shadow, 0, 0, w, h);

    // Above the surface: the dry letters, untouched.
    ctx.save();
    surfacePath(ctx, t, "above");
    ctx.clip();
    ctx.drawImage(dry.canvas, 0, 0);
    ctx.restore();

    // Below: each row slid sideways by a ripple flowing downwards, calm right at the surface, then tinted.
    const top = Math.floor(GEO.lineY * k - lh * SURFACE_AMP * 1.6);
    const row = Math.max(1, Math.round(k * 2));
    const period = lh * RIPPLE_PERIOD, amp = lh * RIPPLE_AMP;
    under.clearRect(0, 0, w, h);
    for (let y = top; y < h; y += row) {
      const depth = y - GEO.lineY * k;
      const fade = Math.max(0, Math.min(1, depth / (lh * CALM_DEPTH)));
      const dx = amp * fade * Math.sin((2 * Math.PI * depth) / period - 2 * Math.PI * RIPPLE_SPEED * t);
      under.drawImage(dry.canvas, 0, y, w, row, dx, y, w, row);
    }
    under.globalCompositeOperation = "source-atop";
    under.fillStyle = TINT;
    under.fillRect(0, 0, w, h);
    under.globalCompositeOperation = "source-over";
    ctx.save();
    surfacePath(ctx, t, "below");
    ctx.clip();
    ctx.drawImage(under.canvas, 0, 0);
    ctx.restore();

    // The bright surface line, only where there are letters.
    line.clearRect(0, 0, w, h);
    line.globalCompositeOperation = "source-over";
    surfacePath(line, t, null);
    line.lineWidth = Math.max(2, lh * LINE_WIDTH);
    line.strokeStyle = "rgb(245, 255, 255)";
    line.lineJoin = "round";
    line.stroke();
    line.globalCompositeOperation = "destination-in";
    line.drawImage(dry.canvas, 0, 0);
    ctx.drawImage(line.canvas, 0, 0);
  };

  resize();
  new ResizeObserver(resize).observe(canvas);
  draw(0);
  canvas.style.opacity = "1";
  still.style.opacity = "0";
  if (!animate) return;

  const t0 = performance.now();
  const frame = (now: number) => {
    draw((now - t0) / 1000);
    requestAnimationFrame(frame);
  };
  requestAnimationFrame(frame);
}
