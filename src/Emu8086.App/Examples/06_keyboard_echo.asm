; Keyboard: echo keys in upper case until ESC is pressed.
; Click the Screen panel and type.
#make_COM#
org 100h

    mov dx, offset hint
    mov ah, 09h
    int 21h
read:
    mov ah, 00h             ; BIOS: wait for a key -> AL = ASCII, AH = scan code
    int 16h
    cmp al, 27              ; ESC ends the program
    je quit
    cmp al, 'a'
    jb show
    cmp al, 'z'
    ja show
    sub al, 32              ; 'a'..'z' -> 'A'..'Z'
show:
    mov ah, 0Eh             ; BIOS teletype output
    int 10h
    jmp read
quit:
    ret

hint db 'Type something (ESC to quit):', 13, 10, '$'
