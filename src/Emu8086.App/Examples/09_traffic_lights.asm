; Traffic lights on port 4 (see the Devices panel).
; Bits 0-2 light 1, 3-5 light 2, 6-8 light 3, 9-11 light 4: red, yellow, green.
#start=traffic_lights.exe#
#make_COM#
org 100h

    mov si, 0
next:
    mov ax, states[si]
    out 4, ax
    mov cx, 000Fh           ; wait about 1 second (CX:DX microseconds)
    mov dx, 4240h
    mov ah, 86h
    int 15h
    add si, 2
    cmp si, count * 2
    jb next
    mov si, 0
    jmp next

;             light: 4  3  2  1   (bits G Y R)
states  dw 0000001100001100b    ; 1,3 green  - 2,4 red
        dw 0000001010001010b    ; 1,3 yellow - 2,4 red
        dw 0000100001100001b    ; 1,3 red    - 2,4 green
        dw 0000010001010001b    ; 1,3 red    - 2,4 yellow
count   equ ($ - states) / 2
