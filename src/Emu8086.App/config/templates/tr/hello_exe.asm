; ===========================================================================
;  Merhaba Dünya - EXE (segmentli) sürüm
; ===========================================================================
;  EXE programlarında kod, veri ve yığın (stack) ayrı SEGMENT'lerde durur.
;  Bu, büyük programlar için daha düzenli bir yapıdır.
; ===========================================================================

#make_EXE#

; ---------------------------------------------------------------------------
;  Veri segmenti: değişkenler burada tanımlanır
; ---------------------------------------------------------------------------
data segment
    mesaj db 'Merhaba Dunya! (EXE)', 13, 10, '$'
ends

; ---------------------------------------------------------------------------
;  Yığın segmenti: PUSH/POP ve CALL/RET bu alanı kullanır
; ---------------------------------------------------------------------------
stack segment
    dw 128 dup(0)           ; 128 kelimelik (256 bayt) boş alan
ends

; ---------------------------------------------------------------------------
;  Kod segmenti
; ---------------------------------------------------------------------------
code segment
start:
    ; EXE başladığında DS veri segmentini göstermez; biz ayarlarız.
    mov ax, data            ; AX <- veri segmentinin adresi
    mov ds, ax              ; DS <- AX  (segment registerına doğrudan
                            ;           sabit yazılamaz, AX üzerinden)

    lea dx, mesaj           ; LEA: mesajın adresini DX'e yükle
    mov ah, 09h             ; DOS: metin yaz
    int 21h

    mov ax, 4C00h           ; AH=4Ch (sonlandır), AL=00h (çıkış kodu)
    int 21h
ends

end start                   ; Program 'start' etiketinden başlar
