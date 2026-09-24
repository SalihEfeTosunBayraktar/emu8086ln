; ===========================================================================
;  Klavyeden isim okuma ve selamlama
; ===========================================================================
;  DOS'un 0Ah servisi ile bir satır okur, sonra "Merhaba, <isim>!" yazar.
;  Program çalışırken sağdaki Ekran sekmesine tıklayıp yazabilirsin.
; ===========================================================================

#make_COM#
org 100h

    mov dx, offset soru
    mov ah, 09h
    int 21h

    ; --- Satır oku (Enter'a kadar) -------------------------------------
    mov dx, offset tampon   ; Tamponun 1. baytı: en fazla kaç karakter
    mov ah, 0Ah             ; DOS: tamponlu klavye girişi
    int 21h                 ; 2. bayta okunan karakter sayısı yazılır

    ; --- Okunan metnin sonuna '$' koy -----------------------------------
    mov bl, tampon[1]       ; BL <- okunan karakter sayısı
    mov bh, 0               ; BX = BL (16 bit indeks için üst bayt 0)
    mov tampon[bx+2], '$'   ; Metin 3. bayttan başlar

    mov dx, offset selam
    mov ah, 09h
    int 21h

    mov dx, offset tampon + 2   ; Okunan isim
    mov ah, 09h
    int 21h

    mov dl, '!'             ; Tek karakter yazdır: DOS 02h servisi
    mov ah, 02h
    int 21h

    mov ax, 4C00h
    int 21h

soru   db 'Adiniz: $'
selam  db 13, 10, 'Merhaba, $'
tampon db 30, ?, 31 dup(0)  ; [en fazla][okunan][karakterler...]
