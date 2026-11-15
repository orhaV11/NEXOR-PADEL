import sys,json; sys.path.insert(0,"src/film"); import muapi
from concurrent.futures import ThreadPoolExecutor
st=json.load(open("work/s2/look/stills.json")); REF=st["toy_2"]
STYLE=("Same stylised 3D designer art-toy style and materials as the picture: a collectible vinyl-and-felt figure with adult proportions, long legs, a head only slightly larger than life, small eyes, thick expressive eyebrows, simple chunky hands, hair as one solid sculpted shape, matte soft skin with a slight handmade texture, clothes of real fabric at toy scale (knit, felt, denim, nylon) with a coarse soft weave. "
       "Character reference sheet picture: plain warm cream studio background, soft even daylight from the upper left, a soft contact shadow on the floor, nothing else in the picture. No letters, numbers, logos or badges anywhere.")
TIMO=("TIMO, the tall figure from the picture, 23: very tall and thin, long legs, stands very straight; dark brown skin, long oval face, round dark-brown eyes, thick straight black eyebrows, wide mouth, clean-shaven; black hair in a short flat-top with sharp edges, one solid shape. "
      "He wears a teal retro track jacket zipped to the chin with a cream collar, cream cuffs and ONE wide cream stripe down each sleeve; straight dark navy trousers that end above the ankle; cream socks; clean white trainers; a tiny square teal backpack worn high on both shoulders; a big chunky sunflower-yellow wristwatch with a blank cream face on his left wrist; a knitted scarf of wide teal and coral blocks with a cream fringe, knotted once at his throat, both ends short.")
LUNA=("LUNA, the short figure from the picture, 22: short and sturdy, she would reach Timo's shoulder, strong legs; warm light-tan skin, round face with full cheeks, a band of freckles across the nose, dark eyes, short straight eyebrows, a wide grin; a blunt chin-length bob with a full fringe, all coral, one solid sculpted shape. "
      "She wears an oversized plain coral retro football shirt with a cream V-collar and cream cuffs, worn loose; baggy light-blue denim shorts to the knee; cream socks pulled up; chunky cream trainers, a little scuffed; the same knitted scarf of wide teal and coral blocks with a cream fringe, worn loose round her neck with both ends hanging long down her front.")
BIG=("BIG YELLOW, the suitcase: a GIANT rounded sunflower-yellow hard-shell suitcase as big as a washing machine, wide and deep, standing upright on four small dark-violet wheels, satin finish (not mirror-shiny), two wide raised ridges running round its middle, a flat top, and one short fixed dark-violet grab handle on the top. No stickers, no locks, no telescopic handle.")
CAT=("THE CAT: a fat ginger street cat, round as a loaf of bread, short legs, cream belly and chin, a thick ginger tail held up like a question mark, half-closed green-gold eyes and a flat, unimpressed face; no collar. Made in the same art-toy style: matte, soft, slightly handmade felt texture.")
J=lambda who,view,ar="3:4": ("nano-banana-pro-edit",{"prompt":f"{STYLE} {who} {view}","images_list":[REF],"aspect_ratio":ar,"resolution":"2k"})
jobs={
 "timo_front":J(TIMO,"Full body, front view, standing straight, arms relaxed at his sides, calm friendly face with a small closed-mouth smile, looking at the camera.","9:16"),
 "timo_34":J(TIMO,"From the knees up, three-quarter view turned to his right, a small smile, looking slightly past the camera."),
 "timo_back":J(TIMO,"Full body, seen from directly behind: the plain back of the teal jacket, the small teal backpack, the flat-top from behind.","9:16"),
 "timo_face":J(TIMO,"Head and shoulders close-up, front view, mouth open in a big happy laugh, eyebrows up."),
 "luna_front":J(LUNA,"Full body, front view, standing with her weight on one hip, arms relaxed, a wide friendly grin, looking at the camera.","9:16"),
 "luna_34":J(LUNA,"From the knees up, three-quarter view turned to her left, grinning, looking slightly past the camera."),
 "luna_back":J(LUNA,"Full body, seen from directly behind: the plain back of the coral shirt, the coral bob from behind, the scarf ends not visible.","9:16"),
 "luna_face":J(LUNA,"Head and shoulders close-up, front view, mouth wide open in a big laugh, eyes closed into happy crescents."),
 "big_34":J(BIG,"Three-quarter view of the suitcase alone, the whole object in the picture."),
 "big_side":J(BIG,"Straight side view of the suitcase alone, the whole object in the picture."),
 "cat_side":J(CAT,"Side view, the whole cat, strolling slowly to the left along the top of a low cream wall.","4:3"),
 "cat_front":J(CAT,"Front view, the whole cat sitting, looking at the camera with half-closed eyes.","1:1"),
}
def run(k):
    ep,pl=jobs[k]
    for _ in range(2):
        try: rid=muapi.submit(ep,pl)
        except Exception as e: return k,None,str(e)[:300]
        res=muapi.wait(rid,every=4,limit=400); url=muapi.output_url(res)
        if url:
            muapi.download(url,f"work/s2/cast/{k}.jpg"); return k,url,"ok"
    return k,None,json.dumps(res)[:300]
only=sys.argv[1:] or list(jobs)
with ThreadPoolExecutor(6) as ex: out=list(ex.map(run,only))
try: d=json.load(open("work/s2/cast/cast.json"))
except Exception: d={}
d.update({k:u for k,u,_ in out if u}); json.dump(d,open("work/s2/cast/cast.json","w"),indent=1)
json.dump({"STYLE":STYLE,"TIMO":TIMO,"LUNA":LUNA,"BIG":BIG,"CAT":CAT},open("work/s2/cast/descriptions.json","w"),indent=1)
for o in out: print(o[0],o[2])
print("balance",muapi.balance()["balance"])
