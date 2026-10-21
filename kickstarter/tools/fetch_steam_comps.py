import json, urllib.request, time, sys
ids = [945360,381210,429050,629820,274940,1966720,2881650,3527290,1568590,820520,3241660,915810,330580,384190,602960,1472560,559650,1181550,4241240,719890,1088090,595280,376210,1681110,3788110,3553750]
rows=[]
def get(u):
    req=urllib.request.Request(u,headers={'User-Agent':'Mozilla/5.0'})
    return json.load(urllib.request.urlopen(req,timeout=30))
for i in ids:
    try:
        d=get(f"https://store.steampowered.com/api/appdetails?appids={i}&cc=us&l=en")[str(i)]
        if not d['success']: rows.append({'id':i,'err':'no details'}); continue
        a=d['data']
        r=get(f"https://store.steampowered.com/appreviews/{i}?json=1&language=all&purchase_type=all&num_per_page=0&filter=all")['query_summary']
        rows.append({'id':i,'name':a['name'],'dev':a.get('developers'),'pub':a.get('publishers'),
          'release':a.get('release_date',{}).get('date'),'coming_soon':a.get('release_date',{}).get('coming_soon'),
          'free':a.get('is_free'),'price':(a.get('price_overview') or {}).get('final_formatted'),
          'initial':(a.get('price_overview') or {}).get('initial_formatted'),
          'genres':[g['description'] for g in a.get('genres',[])],
          'cats':[c['description'] for c in a.get('categories',[]) if c['description'] in ('Multi-player','Online PvP','Online Co-op','PvP','Co-op','Single-player','Remote Play Together')],
          'reviews_total':r.get('total_reviews'),'pos':r.get('total_positive'),'desc':r.get('review_score_desc'),
          'ea': 'Early Access' in [g['description'] for g in a.get('genres',[])]})
    except Exception as e:
        rows.append({'id':i,'err':str(e)})
    time.sleep(1.2)
json.dump(rows,open('steam_comps.json','w'),indent=1)
for r in rows: print(r)
