; ===========================================================================
;  Merhaba Dünya - ilk 8086 assembly programın
; ===========================================================================
;  Noktalı virgülden (;) sonra yazılan her şey YORUMDUR.
;  Assembler yorumları görmezden gelir; onlar sadece senin için.
;
;  Bu program ekrana "Merhaba Dunya!" yazar ve bir tuşa basılmasını bekler.
;
;  Nasıl çalıştırılır?
;    F5  : Derle ve çalıştır
;    F11 : Adım adım çalıştır (her komutta durur)
;    F9  : İmlecin olduğu satıra kesme noktası (breakpoint) koy
;  Sağdaki panellerde registerların nasıl değiştiğini izleyebilirsin.
; ===========================================================================

#make_COM#          ; Çıktı türü: COM programı (tek segment, en basit tür)

org 100h            ; COM programları bellekte 100h (256) adresinden başlar.
                    ; Önceki 256 bayt DOS'un PSP alanıdır.

; ---------------------------------------------------------------------------
;  1) Mesajı ekrana yaz
; ---------------------------------------------------------------------------
    mov dx, offset mesaj    ; DX <- mesajın bellekteki adresi (offset'i)
    mov ah, 09h             ; AH <- 09h : DOS'un "metin yaz" servisi
    int 21h                 ; DOS kesmesini çağır. DOS, DS:DX adresindeki
                            ; metni '$' karakterini görene kadar yazar.

; ---------------------------------------------------------------------------
;  2) Bir tuşa basılmasını bekle
; ---------------------------------------------------------------------------
    mov dx, offset bekle
    mov ah, 09h
    int 21h

    mov ah, 00h             ; AH <- 00h : BIOS "tuş bekle" servisi
    int 16h                 ; Basılan tuşun ASCII kodu AL'ye gelir.

; ---------------------------------------------------------------------------
;  3) Programı bitir
; ---------------------------------------------------------------------------
    mov ah, 4Ch             ; AH <- 4Ch : DOS "programı sonlandır" servisi
    mov al, 0               ; AL <- 0   : çıkış kodu (0 = başarılı)
    int 21h

; ---------------------------------------------------------------------------
;  Veriler
;  DB (Define Byte) bellekte bayt dizisi ayırır.
;  13 = satır başı (CR), 10 = yeni satır (LF), '$' = metin sonu işareti
; ---------------------------------------------------------------------------
mesaj db 'Merhaba Dunya!', 13, 10, '$'
bekle db 'Devam etmek icin bir tusa basin...$'
