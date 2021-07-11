from collections import defaultdict
import json

# adapted from https://github.com/soong-construction/dirt-rally-time-recorder/blob/master/resources/setup-dr2.sql
# length, start_z, track_name
track_data = [
    # Baumholder, Germany
    [5361.90966796875, -2668.4755859375,  'DE, Baumholder, Waldaufstieg'],
    [5882.1796875, -948.372314453125,     'DE, Baumholder, Waldabstieg'],
    [6121.8701171875, -718.9346923828125, 'DE, Baumholder, Kreuzungsring'],
    [5666.25, 539.2579345703125,          'DE, Baumholder, Kreuzungsring reverse'],
    [10699.9599609375, 814.2764892578125, 'DE, Baumholder, Ruschberg'],
    [5855.6796875, 513.0728759765625,     'DE, Baumholder, Verbundsring'],
    [5550.85009765625, 657.1261596679688, 'DE, Baumholder, Verbundsring reverse'],
    [5129.0400390625, 814.3093872070312,  'DE, Baumholder, Innerer Feld-Sprint'],
    [4937.85009765625, 656.46044921875,   'DE, Baumholder, Innerer Feld-Sprint reverse'],
    [11487.189453125, -2668.59033203125,  'DE, Baumholder, Oberstein'],
    [10805.23046875, 513.07177734375,     'DE, Baumholder, Hammerstein'],
    [11551.16015625, 539.3564453125,      'DE, Baumholder, Frauenberg'],

    # Monte Carlo, Monaco
    [10805.220703125, 1276.76611328125,    'MC, Monte Carlo, Route de Turini'],
    [10866.8603515625, -2344.705810546875, 'MC, Monte Carlo, Vallee descendante'],
    [4730.02001953125, 283.7648620605469,  'MC, Monte Carlo, Col de Turini – Sprint en descente'],
    [4729.5400390625, -197.3816375732422,  'MC, Monte Carlo, Col de Turini sprint en Montee'],
    [5175.91015625, -131.84573364257812,   'MC, Monte Carlo, Col de Turini – Descente'],
    [5175.91015625, -467.3677062988281,    'MC, Monte Carlo, Gordolon – Courte montee'],
    [4015.35986328125, -991.9784545898438, 'MC, Monte Carlo, Route de Turini (Descente)'],
    [3952.150146484375, 1276.780517578125, 'MC, Monte Carlo, Approche du Col de Turini – Montee'],
    [9831.4501953125, -467.483154296875,   'MC, Monte Carlo, Pra d´Alart'],
    [9832.0205078125, 283.4727478027344,   'MC, Monte Carlo, Col de Turini Depart'],
    [6843.3203125, -991.945068359375,      'MC, Monte Carlo, Route de Turini (Montee)'],
    [6846.830078125, -2344.592529296875,   'MC, Monte Carlo, Col de Turini – Depart en descente'],

    # Powys, Wales
    [4821.64990234375, 2047.56201171875,   'UK, Powys, Pant Mawr Reverse'],
    [4960.06005859375, 1924.06884765625,   'UK, Powys, Bidno Moorland'],
    [5165.96044921875, 2481.105224609375,  'UK, Powys, Bidno Moorland Reverse'],
    [11435.5107421875, -557.0780029296875, 'UK, Powys, River Severn Valley'],
    [11435.5400390625, 169.15403747558594, 'UK, Powys, Bronfelen'],
    [5717.39990234375, -557.11328125,      'UK, Powys, Fferm Wynt'],
    [5717.3896484375, -22.597640991210938, 'UK, Powys, Fferm Wynt Reverse'],
    [5718.099609375, -23.46375274658203,   'UK, Powys, Dyffryn Afon'],
    [5718.10009765625, 169.0966033935547,  'UK, Powys, Dyffryn Afon Reverse'],
    [9911.66015625, 2220.982177734375,     'UK, Powys, Sweet Lamb'],
    [10063.6005859375, 2481.169677734375,  'UK, Powys, Geufron Forest'],
    [4788.669921875, 2221.004150390625,    'UK, Powys, Pant Mawr'],

    # Värmland, Sweden
    [7055.9501953125, -1618.4476318359375, 'SE, Värmland, Älgsjön'],
    [4911.68017578125, -1742.0498046875,   'SE, Värmland, Östra Hinnsjön'],
    [6666.89013671875, -2143.403076171875, 'SE, Värmland, Stor-jangen Sprint'],
    [6693.43994140625, 563.3468017578125,  'SE, Värmland, Stor-jangen Sprint Reverse'],
    [4931.990234375, -5101.59619140625,    'SE, Värmland, Björklangen'],
    [11922.6201171875, -4328.87158203125,  'SE, Värmland, Ransbysäter'],
    [12123.740234375, 2697.36279296875,    'SE, Värmland, Hamra'],
    [12123.5908203125, -5101.78369140625,  'SE, Värmland, Lysvik'],
    [11503.490234375, 562.8009033203125,   'SE, Värmland, Norraskoga'],
    [5248.35986328125, -4328.87158203125,  'SE, Värmland, Älgsjön Sprint'],
    [7058.47998046875, 2696.98291015625,   'SE, Värmland, Elgsjön'],
    [4804.0302734375, -2143.44384765625,   'SE, Värmland, Skogsrallyt'],

    # New England, USA
    [6575.8701171875, -408.4866027832031,  'US, New England, Tolt Valley Sprint Forward'],
    [6701.61962890625, 1521.6917724609375, 'US, New England, Hancock Creek Burst'],
    [6109.5400390625, -353.0966796875,     'US, New England, Hancock Hill Sprint Reverse'],
    [12228.830078125, 1521.5872802734375,  'US, New England, North Fork Pass'],
    [12276.1201171875, 27.728849411010742, 'US, New England, North Fork Pass Reverse'],
    [6488.330078125, 27.087112426757812,   'US, New England, Fuller Mountain Descent'],
    [6468.2998046875, 2768.10107421875,    'US, New England, Fuller Mountain Ascent'],
    [6681.60986328125, 2950.6044921875,    'US, New England, Fury Lake Depart'],
    [12856.66015625, 518.76123046875,      'US, New England, Beaver Creek Trail Forward'],
    [12765.919921875, -4617.37744140625,   'US, New England, Beaver Creek Trail Reverse'],
    [6229.10986328125, 518.7451171875,     'US, New England, Hancock Hill Sprint Forward'],
    [6604.0302734375, -4617.388671875,     'US, New England, Tolt Valley Sprint Reverse'],

    # Catamarca Province, Argentina
    [7667.31982421875, 131.03880310058594,   'AR, Catamarca, Valle de los puentes'],
    [3494.010009765625, -1876.9149169921875, 'AR, Catamarca, Huillaprima'],
    [8265.9501953125, 205.80775451660156,    'AR, Catamarca, Camino a la Puerta'],
    [8256.8603515625, 2581.345947265625,     'AR, Catamarca, Las Juntas'],
    [5303.7900390625, 2581.339599609375,     'AR, Catamarca, Camino de acantilados y rocas'],
    [4171.5, -3227.17626953125,              'AR, Catamarca, San Isidro'],
    [3353.0400390625, 130.6753692626953,     'AR, Catamarca, Miraflores'],
    [2845.6298828125, 206.18272399902344,    'AR, Catamarca, El Rodeo'],
    [7929.18994140625, -3227.17724609375,    'AR, Catamarca, Valle de los puentes a la inversa'],
    [5294.81982421875, 1379.72607421875,     'AR, Catamarca, Camino de acantilados y rocas inverso'],
    [4082.2998046875, -1864.662109375,       'AR, Catamarca, Camino a Coneta'],
    [2779.489990234375, 1344.307373046875,   'AR, Catamarca, La Merced'],

    # Hawkes Bay, New Zealand
    [4799.84033203125, -4415.70703125,      'NZ, Hawkes Bay, Te Awanga Sprint Forward'],
    [11437.0703125, 1789.1517333984375,     'NZ, Hawkes Bay, Ocean Beach'],
    [6624.0302734375, 1789.0382080078125,   'NZ, Hawkes Bay, Ocean Beach Sprint'],
    [4688.52978515625, -2004.0015869140625, 'NZ, Hawkes Bay, Te Awanga Sprint Reverse'],
    [8807.490234375, 2074.951171875,        'NZ, Hawkes Bay, Waimarama Sprint Forward'],
    [6584.10009765625, -1950.1710205078125, 'NZ, Hawkes Bay, Ocean Beach Sprint Reverse'],
    [7137.81005859375, 2892.6181640625,     'NZ, Hawkes Bay, Elsthorpe Sprint Forward'],
    [15844.529296875, 2074.938720703125,    'NZ, Hawkes Bay, Waimarama Point Reverse'],
    [16057.8505859375, 2892.97216796875,    'NZ, Hawkes Bay, Waimarama Point Forward'],
    [11507.4404296875, -4415.119140625,     'NZ, Hawkes Bay, Te Awanga Forward'],
    [8733.98046875, 5268.0849609375,        'NZ, Hawkes Bay, Waimarama Sprint Reverse'],
    [6643.490234375, 5161.06396484375,      'NZ, Hawkes Bay, Elsthorpe Sprint Reverse'],

    # Poland, Leczna County:
    [6622.080078125, 4644.4375,            'PL, Leczna, Czarny Las'],
    [9254.900390625, 1972.7869873046875,   'PL, Leczna, Marynka'],
    [6698.81005859375, -3314.843505859375, 'PL, Leczna, Lejno'],
    [8159.81982421875, 7583.216796875,     'PL, Leczna, Józefin'],
    [7840.1796875, 4674.87548828125,       'PL, Leczna, Kopina'],
    [6655.5400390625, -402.56207275390625, 'PL, Leczna, Jagodno'],
    [13180.3798828125, -3314.898193359375, 'PL, Leczna, Zienki'],
    [16475.009765625, 4674.9150390625,     'PL, Leczna, Zaróbka'],
    [16615.0, 1973.2518310546875,          'PL, Leczna, Zagorze'],
    [13295.6796875, 4644.3798828125,       'PL, Leczna, Jezioro Rotcze'],
    [9194.3203125, 7393.35107421875,       'PL, Leczna, Borysik'],
    [6437.80029296875, -396.1388854980469, 'PL, Leczna, Jezioro Lukie'],

    # Australia, Monaro:
    [13304.1201171875, 2242.524169921875,   'AU, Monaro, Mount Kaye Pass'],
    [13301.109375, -2352.5615234375,        'AU, Monaro, Mount Kaye Pass Reverse'],
    [6951.15966796875, 2242.5224609375,     'AU, Monaro, Rockton Plains'],
    [7116.14990234375, 2457.100341796875,   'AU, Monaro, Rockton Plains Reverse'],
    [6398.90966796875, 2519.408447265625,   'AU, Monaro, Yambulla Mountain Ascent'],
    [6221.490234375, -2352.546630859375,    'AU, Monaro, Yambulla Mountain Descent'],
    [12341.25, 2049.85888671875,            'AU, Monaro, Chandlers Creek'],
    [12305.0400390625, -1280.10595703125,   'AU, Monaro, Chandlers Creek Reverse'],
    [7052.2998046875, -603.9149169921875,   'AU, Monaro, Bondi Forest'],
    [7007.02001953125, -1280.1004638671875, 'AU, Monaro, Taylor Farm Sprint'],
    [5277.02978515625, 2049.85791015625,    'AU, Monaro, Noorinbee Ridge Ascent'],
    [5236.91015625, -565.1859130859375,     'AU, Monaro, Noorinbee Ridge Descent'],

    # Spain, Ribadelles:
    [14348.3603515625, 190.28546142578125,  'ES, Ribadelles, Comienzo en Bellriu'],
    [10568.4296875, -2326.21142578125,      'ES, Ribadelles, Centenera'],
    [7297.27001953125, 2593.36376953125,    'ES, Ribadelles, Ascenso bosque Montverd'],
    [6194.7099609375, -2979.6650390625,     'ES, Ribadelles, Vinedos Dardenya inversa'],
    [6547.39990234375, -2002.0657958984375, 'ES, Ribadelles, Vinedos Dardenya'],
    [6815.4501953125, -2404.635009765625,   'ES, Ribadelles, Vinedos dentro del valle Parra'],
    [10584.6796875, -2001.96337890625,      'ES, Ribadelles, Camina a Centenera'],
    [4380.740234375, -3003.546630859375,    'ES, Ribadelles, Subida por carretera'],
    [6143.5703125, 2607.470947265625,       'ES, Ribadelles, Salida desde Montverd'],
    [7005.68994140625, 190.13796997070312,  'ES, Ribadelles, Ascenso por valle el Gualet'],
    [4562.80029296875, -2326.251708984375,  'ES, Ribadelles, Descenso por carretera'],
    [13164.330078125, -2404.1171875,        'ES, Ribadelles, Final de Bellriu'],

    # Greece, Argolis
    [4860.1904296875, 91.54808044433594,   'GR, Argolis, Ampelonas Ormi'],
    [9666.5, -2033.0767822265625,          'GR, Argolis, Anodou Farmakas'],
    [9665.990234375, 457.1891784667969,    'GR, Argolis, Kathodo Leontiou'],
    [5086.830078125, -2033.0767822265625,  'GR, Argolis, Pomono Ékrixi'],
    [4582.009765625, 164.40521240234375,   'GR, Argolis, Koryfi Dafni'],
    [4515.39990234375, 457.18927001953125, 'GR, Argolis, Fourketa Kourva'],
    [10487.060546875, 504.3974609375,      'GR, Argolis, Perasma Platani'],
    [10357.8798828125, -3672.5810546875,   'GR, Argolis, Tsiristra Théa'],
    [5739.099609375, 504.3973693847656,    'GR, Argolis, Ourea Spevsi'],
    [5383.009765625, -2277.10986328125,    'GR, Argolis, Ypsona tou Dasos'],
    [6888.39990234375, -1584.236083984375, 'GR, Argolis, Abies Koiláda'],
    [6595.31005859375, -3672.58154296875,  'GR, Argolis, Pedines Epidaxi'],

    # Finland, Jämsä
    [7515.40966796875, 39.52613830566406,  'FI, Jämsä, Kailajärvi'],
    [7461.65966796875, 881.0377197265625,  'FI, Jämsä, Paskuri'],
    [7310.5400390625, 846.68701171875,     'FI, Jämsä, Naarajärvi'],
    [7340.3798828125, -192.40794372558594, 'FI, Jämsä, Jyrkysjärvi'],
    [16205.1904296875, 3751.42236328125,   'FI, Jämsä, Kakaristo'],
    [16205.259765625, 833.2575073242188,   'FI, Jämsä, Pitkäjärvi'],
    [8042.5205078125, 3751.42236328125,    'FI, Jämsä, Iso Oksjärvi'],
    [8057.52978515625, -3270.775390625,    'FI, Jämsä, Oksala'],
    [8147.560546875, -3263.315185546875,   'FI, Jämsä, Kotajärvi'],
    [8147.419921875, 833.2575073242188,    'FI, Jämsä, Järvenkylä'],
    [14929.7998046875, 39.52613067626953,  'FI, Jämsä, Kontinjärvi'],
    [14866.08984375, -192.407958984375,    'FI, Jämsä, Hämelahti'],

    # Scotland, UK
    [7144.69970703125, -1657.4295654296875, 'UK, Scotland, Rosebank Farm'],
    [6967.89990234375, 3383.216796875,      'UK, Scotland, Rosebank Farm Reverse'],
    [12857.0703125, 1386.7626953125,        'UK, Scotland, Newhouse Bridge'],
    [12969.2109375, -403.3143310546875,     'UK, Scotland, Newhouse Bridge Reverse'],
    [5822.77001953125, -1157.0889892578125, 'UK, Scotland, Old Butterstone Muir'],
    [5659.8203125, 3339.01513671875,        'UK, Scotland, Old Butterstone Muir Reverse'],
    [7703.72021484375, -403.3154602050781,  'UK, Scotland, Annbank Station'],
    [7587.64013671875, -1839.5506591796875, 'UK, Scotland, Annbank Station Reverse'],
    [5245.4501953125, 1387.3612060546875,   'UK, Scotland, Glencastle Farm'],
    [5238.43994140625, -1860.2203369140625, 'UK, Scotland, Glencastle Farm Reverse'],
    [12583.41015625, -1157.111083984375,    'UK, Scotland, South Morningside'],
    [12670.58984375, -1656.8243408203125,   'UK, Scotland, South Morningside Reverse'],

    # Rallycross locations:
    [1075.0989990234375, 149.30722045898438,  'RX, BE, Mettet'],
    [1400.0770263671875, -230.09457397460938, 'RX, CA, Trois-Rivieres'],
    [1348.85400390625, 101.5931396484375,     'RX, UK, Lydden Hill'],
    [991.1160278320312, -185.40646362304688,  'RX, UK, Silverstone'],
    [1064.97998046875, 195.76113891601562,    'RX, FR, Loheac'],
    [951.51171875, -17.769332885742188,       'RX, DE, Estering'],
    [1287.4329833984375, 134.0433807373047,   'RX, LV, Bikernieki'],
    [1036.0970458984375, 122.2354736328125,   'RX, NO, Hell'],
    [1026.759033203125, -541.3275756835938,   'RX, PT, Montalegre'],
    [1064.623046875, -100.92737579345703,     'RX, ZA, Killarney'],
    [1119.3590087890625, -341.3289794921875,  'RX, ES, Barcalona-Catalunya'],
    [1207.18798828125, 180.26181030273438,    'RX, SE, Holjes'],
    [1194.22900390625, -133.4615936279297,    'RX, AE, Yas Marina'],

    # Test track locations:
    [3601.22998046875, 121.67539978027344, 'DirtFish'],
]

# track length is not a unique key as some tracks are just reversed
# it's unique together with the starting position, which is not accurate to float precision
track_dict = defaultdict(list)
for t in track_data:
    key = "{:0.2f}".format(t[0])
    track_dict[key].append({"start_z": t[1], "track_name": t[2]})
    with open("track_dict.json", "w", encoding='utf8') as f:
        json.dump(track_dict, f)