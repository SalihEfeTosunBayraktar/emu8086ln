; Boş EXE programı
#make_EXE#

data segment
    ; Değişkenlerini buraya yaz
ends

stack segment
    dw 128 dup(0)
ends

code segment
start:
    mov ax, data
    mov ds, ax
    mov es, ax

    ; Kodunu buraya yaz

    mov ax, 4C00h           ; Programı bitir
    int 21h
ends

end start
