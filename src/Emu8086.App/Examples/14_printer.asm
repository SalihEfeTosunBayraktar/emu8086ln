; Printer: send text with INT 21h function 05h (see Devices > Printer).
#make_COM#
org 100h

    mov si, offset text
next:
    mov dl, [si]
    cmp dl, 0
    je done
    mov ah, 05h
    int 21h
    inc si
    jmp next
done:
    ret

text db 'Printed by emu8086ln', 13, 10, 'Line 2', 13, 10, 0
