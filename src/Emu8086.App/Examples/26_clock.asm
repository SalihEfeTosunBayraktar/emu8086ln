; Shows the current time using DOS function 2Ch, refreshed until a key is pressed.
include 'emu8086.inc'
#make_COM#
org 100h

    CURSOROFF
again:
    GOTOXY 0, 0
    mov ah, 2Ch             ; CH = hour, CL = minute, DH = second
    int 21h
    mov al, ch
    call two_digits
    PUTC ':'
    mov al, cl
    call two_digits
    PUTC ':'
    mov al, dh
    call two_digits

    mov ah, 01h             ; key pressed?
    int 16h
    jz again
    mov ah, 0
    int 16h                 ; remove the key
    CURSORON
    ret

; prints AL (0..99) as two digits
two_digits:
    push ax
    mov ah, 0
    mov bl, 10
    div bl                  ; AL = tens, AH = ones
    add ax, 3030h
    PUTC al
    PUTC ah
    pop ax
    ret
