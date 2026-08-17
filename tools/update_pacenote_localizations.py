#!/usr/bin/env python3
"""Populate the official pacenote display names in every supported UI locale."""

from __future__ import annotations

import argparse
import sqlite3
from pathlib import Path


LOCALES = ("de-de", "es-es", "fr-fr", "it-it", "ja-jp", "ko-kr", "pt-br", "ru-ru")


def entries(raw: str) -> dict[str, str]:
    return dict(line.split("=", 1) for line in raw.strip().splitlines() if line.strip())


# These are concise visual roadbook labels, not a prescribed spoken-note system.
# Core rally calls are intentionally translated as units instead of word-for-word.
DIRECT = {
    "de-de": entries(r"""
acute=Spitzkehre
after=danach
and=und
around=herum
at=bei
at_bush=am Busch
at_junction=an der Kreuzung
bad=heftig
big=groß
brake=bremsen
brave=mutig
care=Vorsicht
caution=Achtung
clip=leicht schneiden
cobble=Pflaster
collision_medium=mittlerer Aufprall
collision_severe=schwerer Aufprall
collision_slight=leichter Aufprall
concrete=Beton
continues=geht weiter
cut=schneiden
dab=kurz bremsen
deceptive=täuschend
deep=tief
directly=direkt
dirty=verschmutzt
distance_call=Distanzansage
dont_cut=nicht schneiden
double=doppelt
double_caution=doppelte Achtung
downhill=bergab
draws_in=zieht sich zu
drops=fällt ab
drops_inside=innen Abhang
drops_outside=außen Abhang
dry=trocken
early=früh
easy=leicht
end_stage=Ziel
engine_damage=Motorschaden
entry=Eingang
exact=genau
exit=Ausgang
extra=extra
extra_long=extra lang
extraextra_long=extrem lang
fast=schnell
fickle=wechselhaft
finish=Ziel
flat=voll
from_left=von links
from_right=von rechts
go=los
go_straight=geradeaus
good=gut
gravel=Schotter
half_long=halb lang
handbrake=Handbremse
hard=schwierig
hold=halten
hook=am Scheitel halten
ice=Eis
important=wichtig
in=innen
inside=innen
into=in
keep_in=innen halten
keep_left=links halten
keep_middle=mittig halten
keep_out=außen halten
keep_right=rechts halten
kinks=Knicke
late=spät
left=links
lift=Gas lupfen
light=leicht
little=etwas
long=lang
loose_gravel=loser Schotter
many=viele
maybe=vielleicht
minus=minus
more=mehr
much=stark
narrow=eng
narrows=wird eng
near=nahe
neat=saubere Linie
onto=auf
opens=öffnet
opens_and_tightens=öffnet und zieht zu
out=außen
outside=außen
outtrack=weit hinaus
pass_junction=Kreuzung passieren
patchy=wechselnd
patient=geduldig
plus=plus
puncture_front_left=Reifenschaden vorne links
puncture_front_right=Reifenschaden vorne rechts
puncture_rear_left=Reifenschaden hinten links
puncture_rear_right=Reifenschaden hinten rechts
radiator_damage=Kühlerschaden
remember=merken
retired=ausgeschieden
right=rechts
rotate=eindrehen
rough=rau
sharp=scharf
short=kurz
slightly=leicht
slippery=rutschig
slow=langsamer
slowing=verlangsamt
small=klein
snow=Schnee
straight=gerade
stop=Stopp
sudden=sofort
take_speed=Tempo aufnehmen
tarmac=Asphalt
then=dann
through=durch
tightens=zieht zu
tightens_bad=zieht stark zu
triple=dreifach
triple_caution=dreifache Achtung
trust=vertrauen
turn=abbiegen
twisty=kurvig
understeer=Untersteuern
unseen=nicht einsehbar
until=bis
very=sehr
verylong=sehr lang
walk=Schritttempo
water_pump_damage=Wasserpumpenschaden
widens=wird breit
"""),
    "es-es": entries(r"""
acute=horquilla cerrada
after=después
and=y
around=rodear
at=en
at_bush=en el arbusto
at_junction=en el cruce
bad=muy fuerte
big=grande
brake=frenar
brave=con decisión
care=cuidado
caution=atención
clip=rozar vértice
cobble=adoquín
collision_medium=impacto medio
collision_severe=impacto fuerte
collision_slight=impacto leve
concrete=hormigón
continues=continúa
cut=cortar
dab=toque de freno
deceptive=engañosa
deep=profundo
directly=directo
dirty=sucio
distance_call=canto de distancia
dont_cut=no cortar
double=doble
double_caution=doble atención
downhill=bajada
draws_in=se recoge
drops=cae
drops_inside=caída interior
drops_outside=caída exterior
dry=seco
early=pronto
easy=fácil
end_stage=meta
engine_damage=avería de motor
entry=entrada
exact=exacto
exit=salida
extra=extra
extra_long=extra larga
extraextra_long=muy muy larga
fast=rápido
fickle=cambiante
finish=meta
flat=a fondo
from_left=desde izquierda
from_right=desde derecha
go=ya
go_straight=seguir recto
good=bueno
gravel=grava
half_long=media larga
handbrake=freno de mano
hard=difícil
hold=mantener
hook=buscar vértice
ice=hielo
important=importante
in=interior
inside=interior
into=seguido de
keep_in=mantener interior
keep_left=mantener izquierda
keep_middle=mantener centro
keep_out=mantener exterior
keep_right=mantener derecha
kinks=eses
late=tarde
left=izquierda
lift=levantar gas
light=leve
little=un poco
long=larga
loose_gravel=grava suelta
many=muchos
maybe=quizá
minus=menos
more=más
much=mucho
narrow=estrecho
narrows=se estrecha
near=cerca
neat=trazada limpia
onto=sobre
opens=abre
opens_and_tightens=abre y cierra
out=exterior
outside=exterior
outtrack=salir ancho
pass_junction=pasar cruce
patchy=por tramos
patient=con paciencia
plus=más
puncture_front_left=pinchazo delantero izquierdo
puncture_front_right=pinchazo delantero derecho
puncture_rear_left=pinchazo trasero izquierdo
puncture_rear_right=pinchazo trasero derecho
radiator_damage=avería de radiador
remember=recordar
retired=abandono
right=derecha
rotate=hacer girar
rough=roto
sharp=cerrado
short=corta
slightly=ligeramente
slippery=deslizante
slow=despacio
slowing=se frena
small=pequeño
snow=nieve
straight=recto
stop=parar
sudden=de inmediato
take_speed=ganar velocidad
tarmac=asfalto
then=luego
through=por
tightens=cierra
tightens_bad=cierra mucho
triple=triple
triple_caution=triple atención
trust=confía
turn=girar
twisty=virado
understeer=subviraje
unseen=sin visibilidad
until=hasta
very=muy
verylong=muy larga
walk=paso lento
water_pump_damage=avería de bomba de agua
widens=se ensancha
"""),
    "fr-fr": entries(r"""
acute=épingle serrée
after=après
and=et
around=contourner
at=à
at_bush=au buisson
at_junction=au carrefour
bad=fort
big=gros
brake=freiner
brave=franc
care=prudence
caution=attention
clip=mordre
cobble=pavés
collision_medium=choc moyen
collision_severe=choc violent
collision_slight=choc léger
concrete=béton
continues=continue
cut=corde
dab=touche de frein
deceptive=piégeux
deep=profond
directly=direct
dirty=sale
distance_call=annonce de distance
dont_cut=pas corde
double=double
double_caution=double attention
downhill=descente
draws_in=se resserre
drops=plonge
drops_inside=vide intérieur
drops_outside=vide extérieur
dry=sec
early=tôt
easy=facile
end_stage=arrivée
engine_damage=moteur endommagé
entry=entrée
exact=précis
exit=sortie
extra=extra
extra_long=extra long
extraextra_long=très très long
fast=rapide
fickle=changeant
finish=arrivée
flat=à fond
from_left=depuis gauche
from_right=depuis droite
go=go
go_straight=tout droit
good=bon
gravel=terre
half_long=mi-long
handbrake=frein à main
hard=difficile
hold=tenir
hook=tenir la corde
ice=verglas
important=important
in=intérieur
inside=intérieur
into=enchaîne
keep_in=rester intérieur
keep_left=rester gauche
keep_middle=rester milieu
keep_out=rester extérieur
keep_right=rester droite
kinks=enchaînement
late=tard
left=gauche
lift=soulager
light=léger
little=un peu
long=long
loose_gravel=gravillons
many=nombreux
maybe=peut-être
minus=moins
more=plus
much=beaucoup
narrow=étroit
narrows=se rétrécit
near=près
neat=trajectoire propre
onto=sur
opens=ouvre
opens_and_tightens=ouvre puis referme
out=extérieur
outside=extérieur
outtrack=sort large
pass_junction=passe carrefour
patchy=par plaques
patient=patient
plus=plus
puncture_front_left=crevaison avant gauche
puncture_front_right=crevaison avant droite
puncture_rear_left=crevaison arrière gauche
puncture_rear_right=crevaison arrière droite
radiator_damage=radiateur endommagé
remember=rappelle-toi
retired=abandon
right=droite
rotate=faire pivoter
rough=défoncé
sharp=serré
short=court
slightly=légèrement
slippery=glissant
slow=ralentir
slowing=ralentit
small=petit
snow=neige
straight=tout droit
stop=stop
sudden=immédiat
take_speed=reprendre vitesse
tarmac=asphalte
then=puis
through=à travers
tightens=referme
tightens_bad=referme fort
triple=triple
triple_caution=triple attention
trust=fais confiance
turn=tourne
twisty=sinueux
understeer=sous-virage
unseen=sans visibilité
until=jusqu'à
very=très
verylong=très long
walk=au pas
water_pump_damage=pompe à eau endommagée
widens=s'élargit
"""),
    "it-it": entries(r"""
acute=tornante stretto
after=dopo
and=e
around=aggira
at=a
at_bush=al cespuglio
at_junction=all'incrocio
bad=forte
big=grande
brake=frena
brave=deciso
care=occhio
caution=attenzione
clip=sfiora la corda
cobble=ciottolato
collision_medium=urto medio
collision_severe=urto grave
collision_slight=urto lieve
concrete=cemento
continues=continua
cut=taglia
dab=tocco di freno
deceptive=ingannevole
deep=profondo
directly=diretto
dirty=sporco
distance_call=chiamata distanza
dont_cut=non tagliare
double=doppio
double_caution=doppia attenzione
downhill=discesa
draws_in=si stringe
drops=scende
drops_inside=vuoto interno
drops_outside=vuoto esterno
dry=asciutto
early=presto
easy=facile
end_stage=fine prova
engine_damage=motore danneggiato
entry=ingresso
exact=preciso
exit=uscita
extra=extra
extra_long=extra lunga
extraextra_long=lunghissima
fast=veloce
fickle=variabile
finish=arrivo
flat=pieno
from_left=da sinistra
from_right=da destra
go=via
go_straight=dritto
good=buono
gravel=sterrato
half_long=mezza lunga
handbrake=freno a mano
hard=difficile
hold=tieni
hook=chiudi alla corda
ice=ghiaccio
important=importante
in=interno
inside=interno
into=subito in
keep_in=tieni interno
keep_left=tieni sinistra
keep_middle=tieni centro
keep_out=tieni esterno
keep_right=tieni destra
kinks=esse
late=tardi
left=sinistra
lift=alza
light=leggero
little=poco
long=lunga
loose_gravel=ghiaia smossa
many=molti
maybe=forse
minus=meno
more=più
much=molto
narrow=stretto
narrows=si restringe
near=vicino
neat=linea pulita
onto=su
opens=apre
opens_and_tightens=apre e chiude
out=esterno
outside=esterno
outtrack=esci largo
pass_junction=passa incrocio
patchy=a tratti
patient=con pazienza
plus=più
puncture_front_left=foratura anteriore sinistra
puncture_front_right=foratura anteriore destra
puncture_rear_left=foratura posteriore sinistra
puncture_rear_right=foratura posteriore destra
radiator_damage=radiatore danneggiato
remember=ricorda
retired=ritirato
right=destra
rotate=fai ruotare
rough=sconnesso
sharp=stretto
short=corta
slightly=leggermente
slippery=scivoloso
slow=rallenta
slowing=rallenta
small=piccolo
snow=neve
straight=dritto
stop=stop
sudden=immediato
take_speed=prendi velocità
tarmac=asfalto
then=poi
through=attraverso
tightens=chiude
tightens_bad=chiude molto
triple=triplo
triple_caution=tripla attenzione
trust=fidati
turn=gira
twisty=tortuoso
understeer=sottosterzo
unseen=cieco
until=fino a
very=molto
verylong=molto lunga
walk=passo d'uomo
water_pump_damage=pompa acqua danneggiata
widens=si allarga
"""),
    "ja-jp": entries(r"""
acute=鋭角ヘアピン
after=後
and=アンド
around=回り込む
at=地点
at_bush=茂み地点
at_junction=ジャンクション地点
bad=強く
big=大
brake=ブレーキ
brave=思い切って
care=警戒
caution=注意
clip=浅くカット
cobble=石畳
collision_medium=中程度の衝突
collision_severe=激しい衝突
collision_slight=軽い衝突
concrete=コンクリート
continues=続く
cut=カット
dab=軽くブレーキ
deceptive=だまし
deep=深い
directly=すぐ
dirty=ダーティ
distance_call=距離コール
dont_cut=カット禁止
double=ダブル
double_caution=ダブル注意
downhill=下り
draws_in=絞り込む
drops=落ちる
drops_inside=内側落ち
drops_outside=外側落ち
dry=ドライ
early=早め
easy=イージー
end_stage=ステージ終了
engine_damage=エンジン損傷
entry=進入
exact=正確
exit=出口
extra=特に
extra_long=エクストラロング
extraextra_long=超ロング
fast=速く
fickle=変化あり
finish=フィニッシュ
flat=全開
from_left=左から
from_right=右から
go=スタート
go_straight=直進
good=良好
gravel=グラベル
half_long=ハーフロング
handbrake=ハンドブレーキ
hard=難しい
hold=キープ
hook=クリップを取る
ice=アイス
important=重要
in=イン
inside=内側
into=すぐ
keep_in=インをキープ
keep_left=左キープ
keep_middle=中央キープ
keep_out=アウトをキープ
keep_right=右キープ
kinks=連続ゆる曲がり
late=遅め
left=左
lift=リフト
light=軽い
little=少し
long=ロング
loose_gravel=ルーズグラベル
many=多数
maybe=たぶん
minus=マイナス
more=さらに
much=大きく
narrow=狭い
narrows=狭まる
near=近く
neat=きれいなライン
onto=路面へ
opens=オープン
opens_and_tightens=開いてからタイト
out=アウト
outside=外側
outtrack=大きく外へ
pass_junction=ジャンクション通過
patchy=まだら
patient=我慢
plus=プラス
puncture_front_left=左前パンク
puncture_front_right=右前パンク
puncture_rear_left=左後パンク
puncture_rear_right=右後パンク
radiator_damage=ラジエーター損傷
remember=覚えて
retired=リタイア
right=右
rotate=向きを変える
rough=ラフ
sharp=鋭い
short=ショート
slightly=わずかに
slippery=スリッピー
slow=減速
slowing=遅くなる
small=小
snow=スノー
straight=ストレート
stop=停止
sudden=即
take_speed=加速
tarmac=ターマック
then=その後
through=通過
tightens=タイト
tightens_bad=かなりタイト
triple=トリプル
triple_caution=トリプル注意
trust=信じて
turn=ターン
twisty=ツイスティ
understeer=アンダーステア
unseen=ブラインド
until=まで
very=かなり
verylong=ベリーロング
walk=徐行
water_pump_damage=ウォーターポンプ損傷
widens=広がる
"""),
    "ko-kr": entries(r"""
acute=급 헤어핀
after=이후
and=앤드
around=돌아
at=지점
at_bush=덤불 지점
at_junction=교차로 지점
bad=강하게
big=큰
brake=브레이크
brave=과감하게
care=조심
caution=주의
clip=살짝 컷
cobble=자갈 포장
collision_medium=중간 충돌
collision_severe=심한 충돌
collision_slight=가벼운 충돌
concrete=콘크리트
continues=계속
cut=컷
dab=브레이크 터치
deceptive=기만적
deep=깊은
directly=즉시
dirty=더러운 노면
distance_call=거리 콜
dont_cut=컷 금지
double=더블
double_caution=더블 주의
downhill=내리막
draws_in=조여짐
drops=떨어짐
drops_inside=안쪽 낭떠러지
drops_outside=바깥쪽 낭떠러지
dry=건조
early=일찍
easy=여유
end_stage=스테이지 종료
engine_damage=엔진 손상
entry=진입
exact=정확히
exit=탈출
extra=특히
extra_long=엑스트라 롱
extraextra_long=초장거리
fast=빠르게
fickle=변화 심함
finish=피니시
flat=풀 스로틀
from_left=왼쪽에서
from_right=오른쪽에서
go=출발
go_straight=직진
good=좋음
gravel=그래블
half_long=하프 롱
handbrake=핸드브레이크
hard=어려움
hold=유지
hook=에이펙스 붙기
ice=빙판
important=중요
in=안쪽
inside=안쪽
into=곧바로
keep_in=안쪽 유지
keep_left=왼쪽 유지
keep_middle=중앙 유지
keep_out=바깥 유지
keep_right=오른쪽 유지
kinks=연속 완만 굴곡
late=늦게
left=좌
lift=리프트
light=가볍게
little=조금
long=롱
loose_gravel=느슨한 그래블
many=다수
maybe=아마
minus=마이너스
more=더
much=많이
narrow=좁은
narrows=좁아짐
near=근처
neat=깔끔한 라인
onto=노면으로
opens=오픈
opens_and_tightens=열렸다 조임
out=바깥
outside=바깥쪽
outtrack=크게 바깥
pass_junction=교차로 통과
patchy=군데군데
patient=기다려
plus=플러스
puncture_front_left=좌측 앞 펑크
puncture_front_right=우측 앞 펑크
puncture_rear_left=좌측 뒤 펑크
puncture_rear_right=우측 뒤 펑크
radiator_damage=라디에이터 손상
remember=기억
retired=리타이어
right=우
rotate=회전 유도
rough=거친 노면
sharp=급한
short=쇼트
slightly=약간
slippery=미끄러움
slow=감속
slowing=느려짐
small=작은
snow=눈길
straight=직선
stop=정지
sudden=즉시
take_speed=가속
tarmac=타막
then=그다음
through=통과
tightens=타이트
tightens_bad=매우 타이트
triple=트리플
triple_caution=트리플 주의
trust=믿어
turn=턴
twisty=연속 굴곡
understeer=언더스티어
unseen=블라인드
until=까지
very=매우
verylong=베리 롱
walk=서행
water_pump_damage=워터펌프 손상
widens=넓어짐
"""),
    "pt-br": entries(r"""
acute=grampo fechado
after=depois
and=e
around=contornar
at=em
at_bush=no arbusto
at_junction=no entroncamento
bad=forte
big=grande
brake=frear
brave=com coragem
care=cuidado
caution=atenção
clip=beliscar a tangência
cobble=paralelepípedo
collision_medium=impacto médio
collision_severe=impacto grave
collision_slight=impacto leve
concrete=concreto
continues=continua
cut=cortar
dab=toque no freio
deceptive=enganosa
deep=profundo
directly=direto
dirty=sujo
distance_call=chamada de distância
dont_cut=não cortar
double=duplo
double_caution=dupla atenção
downhill=descida
draws_in=fecha
drops=cai
drops_inside=queda interna
drops_outside=queda externa
dry=seco
early=cedo
easy=fácil
end_stage=fim do estágio
engine_damage=motor danificado
entry=entrada
exact=exato
exit=saída
extra=extra
extra_long=extra longa
extraextra_long=longuíssima
fast=rápido
fickle=variável
finish=chegada
flat=pé embaixo
from_left=da esquerda
from_right=da direita
go=já
go_straight=seguir reto
good=bom
gravel=cascalho
half_long=meia longa
handbrake=freio de mão
hard=difícil
hold=manter
hook=buscar tangência
ice=gelo
important=importante
in=interno
inside=interno
into=em seguida
keep_in=manter interno
keep_left=manter esquerda
keep_middle=manter meio
keep_out=manter externo
keep_right=manter direita
kinks=esses
late=tarde
left=esquerda
lift=aliviar
light=leve
little=um pouco
long=longa
loose_gravel=cascalho solto
many=muitos
maybe=talvez
minus=menos
more=mais
much=muito
narrow=estreito
narrows=estreita
near=perto
neat=traçado limpo
onto=sobre
opens=abre
opens_and_tightens=abre e fecha
out=externo
outside=externo
outtrack=sair aberto
pass_junction=passar entroncamento
patchy=em trechos
patient=com paciência
plus=mais
puncture_front_left=furo dianteiro esquerdo
puncture_front_right=furo dianteiro direito
puncture_rear_left=furo traseiro esquerdo
puncture_rear_right=furo traseiro direito
radiator_damage=radiador danificado
remember=lembre
retired=abandono
right=direita
rotate=fazer girar
rough=irregular
sharp=fechado
short=curta
slightly=levemente
slippery=escorregadio
slow=reduzir
slowing=reduzindo
small=pequeno
snow=neve
straight=reto
stop=parar
sudden=imediato
take_speed=ganhar velocidade
tarmac=asfalto
then=depois
through=através
tightens=fecha
tightens_bad=fecha muito
triple=triplo
triple_caution=tripla atenção
trust=confie
turn=virar
twisty=sinuoso
understeer=saída de frente
unseen=cego
until=até
very=muito
verylong=muito longa
walk=passo lento
water_pump_damage=bomba d'água danificada
widens=alarga
"""),
    "ru-ru": entries(r"""
acute=острая шпилька
after=после
and=и
around=объехать
at=у
at_bush=у куста
at_junction=на перекрёстке
bad=сильно
big=большой
brake=тормози
brave=смело
care=осторожно
caution=внимание
clip=слегка резать
cobble=булыжник
collision_medium=средний удар
collision_severe=сильный удар
collision_slight=лёгкий удар
concrete=бетон
continues=продолжается
cut=резать
dab=касание тормоза
deceptive=обманчивый
deep=глубокий
directly=сразу
dirty=грязный
distance_call=дистанция
dont_cut=не резать
double=двойной
double_caution=двойное внимание
downhill=спуск
draws_in=сходится
drops=провал
drops_inside=обрыв внутри
drops_outside=обрыв снаружи
dry=сухо
early=рано
easy=легко
end_stage=финиш СУ
engine_damage=повреждение двигателя
entry=вход
exact=точно
exit=выход
extra=особо
extra_long=очень длинный
extraextra_long=сверхдлинный
fast=быстро
fickle=переменный
finish=финиш
flat=полный газ
from_left=слева
from_right=справа
go=старт
go_straight=прямо
good=хорошо
gravel=гравий
half_long=полудлинный
handbrake=ручник
hard=сложно
hold=держать
hook=держать апекс
ice=лёд
important=важно
in=внутри
inside=внутри
into=сразу в
keep_in=держаться внутри
keep_left=держаться слева
keep_middle=держаться по центру
keep_out=держаться снаружи
keep_right=держаться справа
kinks=связка изгибов
late=поздно
left=лево
lift=сбросить газ
light=лёгкий
little=немного
long=длинный
loose_gravel=сыпучий гравий
many=много
maybe=возможно
minus=минус
more=больше
much=сильно
narrow=узко
narrows=сужается
near=рядом
neat=чистая траектория
onto=на
opens=раскрывается
opens_and_tightens=раскрывается и закрывается
out=снаружи
outside=снаружи
outtrack=широко наружу
pass_junction=проехать перекрёсток
patchy=местами
patient=терпеливо
plus=плюс
puncture_front_left=прокол переднего левого
puncture_front_right=прокол переднего правого
puncture_rear_left=прокол заднего левого
puncture_rear_right=прокол заднего правого
radiator_damage=повреждение радиатора
remember=помни
retired=сход
right=право
rotate=довернуть
rough=разбитый
sharp=резкий
short=короткий
slightly=слегка
slippery=скользко
slow=медленно
slowing=замедляется
small=малый
snow=снег
straight=прямо
stop=стоп
sudden=внезапно
take_speed=разгон
tarmac=асфальт
then=затем
through=через
tightens=закрывается
tightens_bad=сильно закрывается
triple=тройной
triple_caution=тройное внимание
trust=верь
turn=поворот
twisty=извилисто
understeer=снос передка
unseen=слепой
until=до
very=очень
verylong=очень длинный
walk=ползком
water_pump_damage=повреждение водяного насоса
widens=расширяется
"""),
}


NOUNS = {
    "de-de": entries("""
bales=Strohballen
banner=Banner
border=Rand
bridge=Brücke
bumps=Wellen
bush=Busch
chicane=Schikane
crest=Kuppe
curb=Randstein
dip=Senke
ditches=Gräben
edge=Kante
fence=Zaun
flag=Flagge
ford=Furt
gate=Tor
grid=Gitter
hill=Anstieg
hole=Loch
house=Haus
houses=Häuser
incline=Steigung
island=Verkehrsinsel
jump=Sprung
junction=Kreuzung
logs=Baumstämme
marshalls=Streckenposten
post=Pfosten
posts=Pfosten
rails=Leitplanke
rocks=Felsen
roundabout=Kreisverkehr
ruts=Spurrillen
shoulder=Bankett
sign=Schild
slope=Hang
spectators=Zuschauer
stake=Pflock
stump=Baumstumpf
tape=Absperrband
tree=Baum
tunnel=Tunnel
tyres=Reifen
wall=Mauer
"""),
    "es-es": entries("""
bales=pacas
banner=pancarta
border=borde
bridge=puente
bumps=baches
bush=arbusto
chicane=chicane
crest=rasante
curb=bordillo
dip=vaguada
ditches=cunetas
edge=borde
fence=valla
flag=bandera
ford=vado
gate=portón
grid=rejilla
hill=cuesta
hole=agujero
house=casa
houses=casas
incline=pendiente
island=isleta
jump=salto
junction=cruce
logs=troncos
marshalls=comisarios
post=poste
posts=postes
rails=guardarraíl
rocks=rocas
roundabout=rotonda
ruts=roderas
shoulder=arcén
sign=señal
slope=pendiente
spectators=público
stake=estaca
stump=tocón
tape=cinta
tree=árbol
tunnel=túnel
tyres=neumáticos
wall=muro
"""),
    "fr-fr": entries("""
bales=ballots
banner=bannière
border=bord
bridge=pont
bumps=bosses
bush=buisson
chicane=chicane
crest=sommet
curb=bordure
dip=creux
ditches=fossés
edge=bord
fence=clôture
flag=drapeau
ford=gué
gate=portail
grid=grille
hill=côte
hole=trou
house=maison
houses=maisons
incline=montée
island=îlot
jump=saut
junction=carrefour
logs=troncs
marshalls=commissaires
post=poteau
posts=poteaux
rails=rail
rocks=rochers
roundabout=rond-point
ruts=ornières
shoulder=accotement
sign=panneau
slope=pente
spectators=spectateurs
stake=piquet
stump=souche
tape=ruban
tree=arbre
tunnel=tunnel
tyres=pneus
wall=mur
"""),
    "it-it": entries("""
bales=balle
banner=striscione
border=bordo
bridge=ponte
bumps=dossi
bush=cespuglio
chicane=chicane
crest=dosso
curb=cordolo
dip=avvallamento
ditches=fossi
edge=bordo
fence=recinzione
flag=bandiera
ford=guado
gate=cancello
grid=griglia
hill=salita
hole=buca
house=casa
houses=case
incline=pendenza
island=isola
jump=salto
junction=incrocio
logs=tronchi
marshalls=commissari
post=palo
posts=pali
rails=guardrail
rocks=rocce
roundabout=rotonda
ruts=solchi
shoulder=banchina
sign=cartello
slope=pendenza
spectators=spettatori
stake=picchetto
stump=ceppo
tape=nastro
tree=albero
tunnel=galleria
tyres=pneumatici
wall=muro
"""),
    "ja-jp": entries("""
bales=ベール
banner=バナー
border=境界
bridge=橋
bumps=バンプ
bush=茂み
chicane=シケイン
crest=クレスト
curb=縁石
dip=ディップ
ditches=側溝
edge=路肩端
fence=フェンス
flag=旗
ford=渡河
gate=ゲート
grid=グリッド
hill=坂
hole=穴
house=家
houses=家並み
incline=上り勾配
island=アイランド
jump=ジャンプ
junction=ジャンクション
logs=丸太
marshalls=オフィシャル
post=ポスト
posts=ポスト
rails=ガードレール
rocks=岩
roundabout=ラウンドアバウト
ruts=わだち
shoulder=路肩
sign=標識
slope=斜面
spectators=観客
stake=杭
stump=切り株
tape=テープ
tree=木
tunnel=トンネル
tyres=タイヤ
wall=壁
"""),
    "ko-kr": entries("""
bales=건초더미
banner=배너
border=경계
bridge=다리
bumps=범프
bush=덤불
chicane=시케인
crest=크레스트
curb=연석
dip=딥
ditches=도랑
edge=가장자리
fence=울타리
flag=깃발
ford=도섭
gate=게이트
grid=그리드
hill=언덕
hole=구멍
house=집
houses=주택가
incline=오르막 경사
island=교통섬
jump=점프
junction=교차로
logs=통나무
marshalls=오피셜
post=기둥
posts=기둥
rails=가드레일
rocks=바위
roundabout=로터리
ruts=바퀴 자국
shoulder=갓길
sign=표지판
slope=경사
spectators=관중
stake=말뚝
stump=그루터기
tape=테이프
tree=나무
tunnel=터널
tyres=타이어
wall=벽
"""),
    "pt-br": entries("""
bales=fardos
banner=faixa
border=borda
bridge=ponte
bumps=ondulações
bush=arbusto
chicane=chicane
crest=crista
curb=meio-fio
dip=depressão
ditches=valetas
edge=borda
fence=cerca
flag=bandeira
ford=vau
gate=portão
grid=grade
hill=subida
hole=buraco
house=casa
houses=casas
incline=rampa
island=ilha
jump=salto
junction=entroncamento
logs=troncos
marshalls=fiscais
post=poste
posts=postes
rails=guard rail
rocks=pedras
roundabout=rotatória
ruts=trilhos
shoulder=acostamento
sign=placa
slope=inclinação
spectators=público
stake=estaca
stump=toco
tape=fita
tree=árvore
tunnel=túnel
tyres=pneus
wall=muro
"""),
    "ru-ru": entries("""
bales=тюки
banner=баннер
border=край
bridge=мост
bumps=неровности
bush=куст
chicane=шикана
crest=гребень
curb=бордюр
dip=яма
ditches=канавы
edge=край
fence=забор
flag=флаг
ford=брод
gate=ворота
grid=решётка
hill=подъём
hole=яма
house=дом
houses=дома
incline=уклон
island=островок
jump=прыжок
junction=перекрёсток
logs=брёвна
marshalls=маршалы
post=столб
posts=столбы
rails=ограждение
rocks=камни
roundabout=круг
ruts=колеи
shoulder=обочина
sign=знак
slope=склон
spectators=зрители
stake=колышек
stump=пень
tape=лента
tree=дерево
tunnel=тоннель
tyres=шины
wall=стена
"""),
}


PREFIX = {
    "de-de": {"around": "um {0}", "between": "zwischen {0}", "over": "über {0}", "through": "durch {0}", "inside": "{0} innen", "outside": "{0} außen"},
    "es-es": {"around": "rodear {0}", "between": "entre {0}", "over": "sobre {0}", "through": "por {0}", "inside": "{0} interior", "outside": "{0} exterior"},
    "fr-fr": {"around": "contourner {0}", "between": "entre {0}", "over": "sur {0}", "through": "à travers {0}", "inside": "{0} intérieur", "outside": "{0} extérieur"},
    "it-it": {"around": "aggira {0}", "between": "tra {0}", "over": "su {0}", "through": "attraverso {0}", "inside": "{0} interno", "outside": "{0} esterno"},
    "ja-jp": {"around": "{0}を回る", "between": "{0}の間", "over": "{0}越え", "through": "{0}通過", "inside": "内側{0}", "outside": "外側{0}"},
    "ko-kr": {"around": "{0} 돌아", "between": "{0} 사이", "over": "{0} 넘어", "through": "{0} 통과", "inside": "안쪽 {0}", "outside": "바깥쪽 {0}"},
    "pt-br": {"around": "contornar {0}", "between": "entre {0}", "over": "sobre {0}", "through": "através de {0}", "inside": "{0} interno", "outside": "{0} externo"},
    "ru-ru": {"around": "объехать {0}", "between": "между {0}", "over": "через {0}", "through": "через {0}", "inside": "{0} внутри", "outside": "{0} снаружи"},
}


CORNER = {
    "de-de": {"left": "links", "right": "rechts", "hairpin": "Kehre", "open_hairpin": "offene Kehre", "square": "90 Grad", "flat": "voll", "slight": "leicht", "twisty": "kurvig", "opens": "öffnet auf", "tightens": "zieht zu auf"},
    "es-es": {"left": "izquierda", "right": "derecha", "hairpin": "horquilla", "open_hairpin": "horquilla abierta", "square": "90 grados", "flat": "a fondo", "slight": "leve", "twisty": "virada", "opens": "abre a", "tightens": "cierra a"},
    "fr-fr": {"left": "gauche", "right": "droite", "hairpin": "épingle", "open_hairpin": "épingle ouverte", "square": "équerre", "flat": "à fond", "slight": "léger", "twisty": "sinueux", "opens": "ouvre sur", "tightens": "referme sur"},
    "it-it": {"left": "sinistra", "right": "destra", "hairpin": "tornante", "open_hairpin": "tornante aperto", "square": "90 gradi", "flat": "pieno", "slight": "lieve", "twisty": "tortuosa", "opens": "apre a", "tightens": "chiude a"},
    "ja-jp": {"left": "左", "right": "右", "hairpin": "ヘアピン", "open_hairpin": "オープンヘアピン", "square": "スクエア", "flat": "全開", "slight": "緩い", "twisty": "ツイスティ", "opens": "オープンして", "tightens": "タイトして"},
    "ko-kr": {"left": "좌", "right": "우", "hairpin": "헤어핀", "open_hairpin": "오픈 헤어핀", "square": "스퀘어", "flat": "풀", "slight": "완만한", "twisty": "연속 굴곡", "opens": "열려", "tightens": "조여"},
    "pt-br": {"left": "esquerda", "right": "direita", "hairpin": "grampo", "open_hairpin": "grampo aberto", "square": "90 graus", "flat": "pé embaixo", "slight": "leve", "twisty": "sinuosa", "opens": "abre para", "tightens": "fecha para"},
    "ru-ru": {"left": "лево", "right": "право", "hairpin": "шпилька", "open_hairpin": "открытая шпилька", "square": "90 градусов", "flat": "полный", "slight": "лёгкий", "twisty": "извилистый", "opens": "раскрывается в", "tightens": "закрывается в"},
}


DIRECTIONAL_CORNERS = {
    "hairpin_left": ("Kehre links", "horquilla izquierda", "épingle gauche", "tornante sinistro", "左ヘアピン", "좌 헤어핀", "grampo à esquerda", "левая шпилька"),
    "hairpin_right": ("Kehre rechts", "horquilla derecha", "épingle droite", "tornante destro", "右ヘアピン", "우 헤어핀", "grampo à direita", "правая шпилька"),
    "open_hairpin_left": ("offene Kehre links", "horquilla abierta izquierda", "épingle ouverte gauche", "tornante aperto sinistro", "左オープンヘアピン", "좌 오픈 헤어핀", "grampo aberto à esquerda", "открытая левая шпилька"),
    "open_hairpin_right": ("offene Kehre rechts", "horquilla abierta derecha", "épingle ouverte droite", "tornante aperto destro", "右オープンヘアピン", "우 오픈 헤어핀", "grampo aberto à direita", "открытая правая шпилька"),
    "square_left": ("90 Grad links", "90 grados izquierda", "équerre gauche", "90 gradi sinistra", "左スクエア", "좌 스퀘어", "90 graus esquerda", "90 градусов влево"),
    "square_right": ("90 Grad rechts", "90 grados derecha", "équerre droite", "90 gradi destra", "右スクエア", "우 스퀘어", "90 graus direita", "90 градусов вправо"),
    "left_acute": ("Spitzkehre links", "horquilla cerrada izquierda", "épingle serrée gauche", "tornante stretto sinistro", "左鋭角ヘアピン", "좌 급 헤어핀", "grampo fechado à esquerda", "острая левая шпилька"),
    "right_acute": ("Spitzkehre rechts", "horquilla cerrada derecha", "épingle serrée droite", "tornante stretto destro", "右鋭角ヘアピン", "우 급 헤어핀", "grampo fechado à direita", "острая правая шпилька"),
}


ROAD = {
    "bad_camber": ("Gegenneigung", "contraperalte", "dévers", "contropendenza", "逆バンク", "역캠버", "contra-inclinação", "обратный уклон"),
    "bad_dip": ("harte Senke", "vaguada mala", "creux marqué", "avvallamento forte", "深いディップ", "깊은 딥", "depressão forte", "жёсткая яма"),
    "camber": ("Querneigung", "peralte", "dévers", "pendenza", "バンク", "캠버", "inclinação", "уклон"),
    "dip": ("Senke", "vaguada", "creux", "avvallamento", "ディップ", "딥", "depressão", "яма"),
    "grid": ("Gitter", "rejilla", "grille", "griglia", "グリッド", "그리드", "grade", "решётка"),
    "hole": ("Loch", "agujero", "trou", "buca", "穴", "구멍", "buraco", "яма"),
    "jump_maybe": ("möglicher Sprung", "posible salto", "saut possible", "possibile salto", "ジャンプ注意", "점프 가능", "possível salto", "возможен прыжок"),
    "junction": ("Kreuzung", "cruce", "carrefour", "incrocio", "ジャンクション", "교차로", "entroncamento", "перекрёсток"),
    "over_grid": ("über Gitter", "sobre rejilla", "sur grille", "su griglia", "グリッド越え", "그리드 넘어", "sobre grade", "через решётку"),
    "ruts": ("Spurrillen", "roderas", "ornières", "solchi", "わだち", "바퀴 자국", "trilhos", "колеи"),
    "slope": ("Hang", "pendiente", "pente", "pendenza", "斜面", "경사", "inclinação", "склон"),
    "steep_hill": ("steiler Anstieg", "cuesta fuerte", "forte montée", "salita ripida", "急坂", "급경사 언덕", "subida íngreme", "крутой подъём"),
    "steep_incline": ("steile Steigung", "pendiente fuerte", "forte pente", "pendenza ripida", "急勾配", "급한 오르막", "rampa íngreme", "крутой уклон"),
    "steep_slope": ("steiler Hang", "ladera fuerte", "pente raide", "scarpata ripida", "急斜面", "급경사", "inclinação forte", "крутой склон"),
    "through_dip": ("durch Senke", "por vaguada", "dans le creux", "nell'avvallamento", "ディップ通過", "딥 통과", "pela depressão", "через яму"),
    "through_grid": ("durch Gitter", "por rejilla", "sur grille", "su griglia", "グリッド通過", "그리드 통과", "pela grade", "через решётку"),
    "through_junction": ("durch Kreuzung", "por cruce", "traverse carrefour", "attraversa incrocio", "ジャンクション通過", "교차로 통과", "pelo entroncamento", "через перекрёсток"),
    "through_narrow_grid": ("durch enges Gitter", "por rejilla estrecha", "sur grille étroite", "su griglia stretta", "狭いグリッド通過", "좁은 그리드 통과", "pela grade estreita", "через узкую решётку"),
    "through_tunnel": ("durch Tunnel", "por túnel", "dans le tunnel", "nella galleria", "トンネル通過", "터널 통과", "pelo túnel", "через тоннель"),
    "tunnel": ("Tunnel", "túnel", "tunnel", "galleria", "トンネル", "터널", "túnel", "тоннель"),
    "uphill": ("bergauf", "subida", "montée", "salita", "上り", "오르막", "subida", "подъём"),
}


SURFACE = {
    "cobble": ("Pflaster", "adoquín", "pavés", "ciottolato", "石畳", "자갈 포장", "paralelepípedo", "булыжник"),
    "concrete": ("Beton", "hormigón", "béton", "cemento", "コンクリート", "콘크리트", "concreto", "бетон"),
    "dirty": ("verschmutzt", "sucio", "sale", "sporco", "ダーティ", "더러운 노면", "sujo", "грязный"),
    "dry": ("trocken", "seco", "sec", "asciutto", "ドライ", "건조", "seco", "сухо"),
    "gravel": ("Schotter", "grava", "terre", "sterrato", "グラベル", "그래블", "cascalho", "гравий"),
    "ice": ("Eis", "hielo", "verglas", "ghiaccio", "アイス", "빙판", "gelo", "лёд"),
    "loose_gravel": ("loser Schotter", "grava suelta", "gravillons", "ghiaia smossa", "ルーズグラベル", "느슨한 그래블", "cascalho solto", "сыпучий гравий"),
    "patchy": ("wechselnd", "por tramos", "par plaques", "a tratti", "まだら", "군데군데", "em trechos", "местами"),
    "rough": ("rau", "roto", "défoncé", "sconnesso", "ラフ", "거친 노면", "irregular", "разбитый"),
    "slippery": ("rutschig", "deslizante", "glissant", "scivoloso", "スリッピー", "미끄러움", "escorregadio", "скользко"),
    "snow": ("Schnee", "nieve", "neige", "neve", "スノー", "눈길", "neve", "снег"),
    "tarmac": ("Asphalt", "asfalto", "asphalte", "asfalto", "ターマック", "타막", "asfalto", "асфальт"),
}


SPECIAL = {
    "big_cut": ("stark schneiden", "cortar mucho", "grosse corde", "taglia molto", "深くカット", "깊게 컷", "cortar bastante", "сильно резать"),
    "over": ("über", "sobre", "sur", "su", "越え", "넘어", "sobre", "через"),
    "small_cut": ("leicht schneiden", "cortar poco", "petite corde", "taglia poco", "浅くカット", "살짝 컷", "cortar pouco", "слегка резать"),
    "steep": ("steil", "pronunciado", "raide", "ripido", "急", "급경사", "íngreme", "крутой"),
    "start_stage": ("Start der Wertungsprüfung", "inicio del tramo", "départ de spéciale", "inizio prova", "ステージ開始", "스테이지 시작", "início do estágio", "старт СУ"),
}


def corner_label(token: str, locale: str) -> str | None:
    c = CORNER[locale]
    if token in DIRECTIONAL_CORNERS:
        return DIRECTIONAL_CORNERS[token][LOCALES.index(locale)]
    if token[0].isdigit() and token[1:] in ("_left", "_right"):
        return f"{c[token[2:]]} {token[0]}"
    if token in ("left", "right"):
        return c[token]
    if token in ("flat_left", "flat_right", "slight_left", "slight_right", "twisty_left", "twisty_right"):
        kind, side = token.split("_", 1)
        return f"{c[side]} {c[kind]}"
    if token == "hairpin":
        return c["hairpin"]
    if token == "open_hairpin":
        return c["open_hairpin"]
    if token == "square":
        return c["square"]
    if token == "acute":
        return DIRECT[locale]["acute"]
    if token in ("donut", "reverse_donut"):
        clockwise = token == "donut"
        labels = {
            "de-de": ("Donut im Uhrzeigersinn", "Donut gegen Uhrzeigersinn"),
            "es-es": ("rotonda horaria", "rotonda antihoraria"),
            "fr-fr": ("tour horaire", "tour antihoraire"),
            "it-it": ("giro orario", "giro antiorario"),
            "ja-jp": ("時計回りドーナツ", "反時計回りドーナツ"),
            "ko-kr": ("시계 방향 도넛", "반시계 방향 도넛"),
            "pt-br": ("volta horária", "volta anti-horária"),
            "ru-ru": ("оборот по часовой", "оборот против часовой"),
        }
        return labels[locale][0 if clockwise else 1]
    if token == "opens_to":
        return c["opens"]
    if token == "tightens_to":
        return c["tightens"]
    for prefix, key in (("opens_to_", "opens"), ("tightens_to_", "tightens")):
        if token.startswith(prefix):
            target = token[len(prefix):]
            target_label = target if target.isdigit() else {
                "acute": DIRECT[locale]["acute"],
                "flat": c["flat"],
                "hairpin": c["hairpin"],
                "square": c["square"],
                "squre": c["square"],
            }[target]
            return f"{c[key]} {target_label}"
    return None


def obstacle_label(token: str, locale: str) -> str | None:
    nouns = NOUNS[locale]
    if token in nouns:
        return nouns[token]
    for prefix in ("around", "between", "over", "through"):
        marker = prefix + "_"
        if token.startswith(marker):
            rest = token[len(marker):]
            adjective = None
            for candidate in ("very_long", "double", "big", "long", "narrow", "small"):
                if rest.startswith(candidate + "_"):
                    adjective, rest = candidate, rest[len(candidate) + 1:]
                    break
            if rest in nouns:
                noun = nouns[rest]
                if adjective:
                    adjective_label = {
                        "very_long": DIRECT[locale]["very"] + " " + DIRECT[locale]["long"],
                        "double": DIRECT[locale]["double"],
                        "big": DIRECT[locale]["big"],
                        "long": DIRECT[locale]["long"],
                        "narrow": DIRECT[locale]["narrow"],
                        "small": DIRECT[locale]["small"],
                    }[adjective]
                    noun = f"{adjective_label} {noun}"
                return PREFIX[locale][prefix].format(noun)
    for suffix in ("inside", "outside"):
        marker = "_" + suffix
        if token.endswith(marker) and token[:-len(marker)] in nouns:
            return PREFIX[locale][suffix].format(nouns[token[:-len(marker)]])
    if token in ("big_crest", "small_crest", "long_crest", "big_jump", "small_jump"):
        adjective, noun = token.split("_", 1)
        return f"{DIRECT[locale][adjective]} {nouns[noun]}"
    special = {
        "left_entry_chicane": ("Schikane links einfahren", "chicane entrada izquierda", "chicane entrée gauche", "chicane ingresso sinistra", "シケイン左進入", "시케인 좌 진입", "chicane entrada esquerda", "шикана вход слева"),
        "right_entry_chicane": ("Schikane rechts einfahren", "chicane entrada derecha", "chicane entrée droite", "chicane ingresso destra", "シケイン右進入", "시케인 우 진입", "chicane entrada direita", "шикана вход справа"),
    }
    if token in special:
        return special[token][LOCALES.index(locale)]
    return None


def translate(token: str, token_type: str, locale: str) -> str:
    if token_type == "NUMBERS":
        return token
    direct = DIRECT[locale]
    if token in direct:
        return direct[token]
    if token in SPECIAL:
        return SPECIAL[token][LOCALES.index(locale)]
    if token_type == "CORNERS":
        translated = corner_label(token, locale)
        if translated:
            return translated
    if token_type == "OBSTACLES":
        translated = obstacle_label(token, locale)
        if translated:
            return translated
    if token_type == "ROAD" and token in ROAD:
        return ROAD[token][LOCALES.index(locale)]
    if token_type == "SURFACE" and token in SURFACE:
        return SURFACE[token][LOCALES.index(locale)]
    raise KeyError(f"Missing {locale} translation for {token_type}/{token}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "database",
        nargs="?",
        type=Path,
        default=Path(__file__).parents[1] / "src" / "ZTMZ.PacenoteTool.Base" / "data.zdb",
    )
    args = parser.parse_args()

    connection = sqlite3.connect(args.database)
    rows = connection.execute(
        """
        SELECT p.id, f.filename, t.name
        FROM pacenote p
        JOIN pacenote_filenames f ON f.id = p.id AND f.is_primary = 1
        JOIN pacenote_type t ON t.id = p.type
        ORDER BY p.id
        """
    ).fetchall()

    translated = []
    for locale in LOCALES:
        for pacenote_id, token, token_type in rows:
            translated.append((pacenote_id, locale, translate(token, token_type, locale)))

    with connection:
        connection.executemany(
            "DELETE FROM pacenote_localizations WHERE locale = ?",
            ((locale,) for locale in LOCALES),
        )
        connection.executemany(
            """
            INSERT INTO pacenote_localizations(pacenote_id, locale, display_name, description)
            VALUES (?, ?, ?, NULL)
            """,
            translated,
        )
    print(f"Wrote {len(translated)} localized pacenote labels to {args.database}")


if __name__ == "__main__":
    main()
