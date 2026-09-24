; Installing your own interrupt handler: INT 60h prints the character in AL twice.
#make_COM#
org 100h

    push ds
    xor ax, ax
    mov ds, ax                          ; the vector table is at 0000:0000
    mov word ptr ds:[60h*4], offset handler
    mov ds:[60h*4+2], cs                ; vector = segment:offset of the handler
    pop ds

    mov al, 'A'
    int 60h
    mov al, 'B'
    int 60h
    ret

handler:
    push ax
    mov ah, 0Eh
    int 10h
    int 10h
    pop ax
    iret                                ; returns and restores the flags
