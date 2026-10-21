# Tension layers: how they were made

`python tension.py <outdir>` (needs numpy) writes drone.wav, near.wav and sting.wav. They were then finished with ffmpeg:

    ffmpeg -i drone.wav -af "lowpass=f=420,aecho=0.8:0.7:180|420:0.35|0.22,atrim=32:64,asetpts=N/SR/TB,volume=-1.3dB" -c:a libvorbis -q:a 5 Tension_Drone.ogg
    ffmpeg -i near.wav  -af "highpass=f=120,lowpass=f=2600,aecho=0.8:0.75:140|330|610:0.4|0.28|0.18,atrim=16:32,asetpts=N/SR/TB,volume=3.3dB" -c:a libvorbis -q:a 5 Tension_Near.ogg
    ffmpeg -i sting.wav -af "lowpass=f=3200,aecho=0.8:0.7:120|300:0.35|0.2,apad=pad_dur=1.5,volume=3.6dB" -c:a libvorbis -q:a 5 Tension_Sting.ogg

The loops are rendered twice and the second pass is kept, so the echo tail wraps and the loop is seamless.
Water/AmbientDeep.ogg is "Aquaria" (cynicmusic, CC0): `-af "asetrate=35280,aresample=44100,lowpass=f=1100,volume=-6.7dB"`.
