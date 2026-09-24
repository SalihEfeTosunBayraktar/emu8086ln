; Graphics: VGA mode 13h (320x200, 256 colours). Pixels are bytes at A000:0000.
#make_COM#
org 100h

    mov ax, 0013h           ; set video mode 13h
    int 10h

    mov ax, 0A000h
    mov es, ax

    ; colour gradient: pixel colour = (x + y) mod 256
    xor di, di
    mov dx, 0               ; y
row:
    mov cx, 0               ; x
col:
    mov ax, cx
    add ax, dx
    stosb
    inc cx
    cmp cx, 320
    jb col
    inc dx
    cmp dx, 200
    jb row

    ; white diagonal line using BIOS put-pixel
    mov cx, 0
line:
    mov dx, cx
    shr dx, 1
    mov ax, 0C0Fh           ; AH=0Ch put pixel, AL=15 white
    int 10h
    inc cx
    cmp cx, 320
    jb line

    mov ah, 0               ; wait for a key
    int 16h
    mov ax, 0003h           ; back to text mode
    int 10h
    ret
