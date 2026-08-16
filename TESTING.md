# Lorekeeper 1.2.0.0 - Test Release

Status: gotowy do testów.

## Silniki tłumaczeń
- OpenAI
- LibreTranslate

## Lorekeeper Cloud
- wspólna biblioteka zawiera WYŁĄCZNIE tłumaczenia OpenAI,
- LibreTranslate nigdy nie wysyła tłumaczeń do Cloud,
- Cloud HIT ma pierwszeństwo przed lokalnym Libre,
- przy awarii Cloud plugin działa dalej lokalnie,
- Cloud lookup ma minimum 1800 ms timeout.

## Kolejność
1. lokalny cache OpenAI
2. Lorekeeper Cloud (OpenAI)
3. lokalny cache Libre, jeśli wybrano Libre
4. nowe tłumaczenie wybranym silnikiem

## LibreTranslate
- automatyczna instalacja lokalnego runtime,
- bez ręcznego instalowania Pythona i Dockera,
- pasek postępu instalacji,
- osobny cache translations-libre.json.

## OpenAI
- osobny cache translations.json,
- dropdown wspieranych modeli,
- tłumaczenia OpenAI są synchronizowane z Lorekeeper Cloud.

## Cloud endpoint
https://lorekeeper-cloud.heiyeshi.workers.dev

## Prywatność
Klucz OpenAI pozostaje lokalnie i nie jest wysyłany do Lorekeeper Cloud.

Wersja testowa: 1.2.0.0
