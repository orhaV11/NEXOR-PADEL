"""Judge music without ears: ask the listening model BLIND where a track comes from.

    python3 src/film/s2/blind_music.py <scratch_dir> <name> [<name> ...]     names of mp3s in src/film/assets/s2/music/

Each track (26 s from second 6, on a black picture) is put to the listening model twice, in two differently worded
questions: is this a sound people use in the apps, or stock music a brand bought? A track is worth using only if both
answers say it is a real app sound. Answers are printed and saved to <scratch_dir>/result_<first name>.json.
Costs two watcher questions per track.
"""
import sys, json, subprocess, concurrent.futures as cf
sys.path.insert(0,"src/film"); import watch
S=sys.argv[1]; names=sys.argv[2:]
Q1=("Listen. There is no picture on purpose. Where does this music most likely come from? Pick exactly ONE letter first: "
    "(A) a sound that is trending on TikTok or Reels right now, the kind used under dance, meme or football edits; (B) royalty-free library music made for a brand advert or a corporate video; "
    "(C) a children's show or a mobile game; (D) an ordinary radio pop song. Then say the genre, the year it sounds like, and a score 1-10 for how likely a 20-year-old is to say 'this slaps'. Be picky and honest, not polite.")
Q2=("You are 21 and scroll TikTok four hours a day. This audio plays under a funny 25-second video; you cannot see it. First word of your answer must be REAL (it sounds like a real sound people use in the app) or STOCK (it sounds like stock music a brand bought). "
    "Then one sentence why, then the genre, the year it sounds like, and whether it makes you want to move (1-10).")
def prep(n):
    out=f"{S}/{n}.mp4"
    subprocess.run(["ffmpeg","-v","error","-y","-f","lavfi","-i","color=c=black:s=320x568:r=5","-ss","6","-t","26","-i",f"src/film/assets/s2/music/{n}.mp3","-shortest","-c:v","libx264","-c:a","aac","-b:a","160k",out],check=True)
    return out
def one(a):
    n,q=a
    try: return n,q,watch.ask(prep(n), Q1 if q==1 else Q2)
    except Exception as e: return n,q,"ERROR "+str(e)[:200]
res={}
with cf.ThreadPoolExecutor(5) as ex:
    for n,q,a in ex.map(one,[(n,q) for n in names for q in (1,2)]):
        res.setdefault(n,{})[f"q{q}"]=a
        print("=====",n,"Q",q,"\n",a[:700],"\n",flush=True)
json.dump(res,open(f"{S}/result_{names[0]}.json","w"),indent=1,ensure_ascii=False)
