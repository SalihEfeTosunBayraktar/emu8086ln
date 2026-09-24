; Strings: reverse a text in place using SI (start) and DI (end).
#make_COM#
org 100h

    mov si, offset text
    mov di, offset text + text_len - 1
swap:
    cmp si, di
    jae done
    mov al, [si]            ; exchange the characters at SI and DI
    mov bl, [di]
    mov [si], bl
    mov [di], al
    inc si
    dec di
    jmp swap
done:
    mov dx, offset text
    mov ah, 09h
    int 21h
    ret

text   db 'emu8086ln is fun'
text_len equ $ - text       ; $ is the current address: text_len = 16
       db '$'
